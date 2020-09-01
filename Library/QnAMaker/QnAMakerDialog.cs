// 
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license.
// 
// Microsoft Bot Framework: http://botframework.com
// 
// Cognitive Services based Dialogs for Bot Builder:
// https://github.com/Microsoft/BotBuilder-CognitiveServices 
// 
// Copyright (c) Microsoft Corporation
// All rights reserved.
// 
// MIT License:
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Builder.Internals.Fibers;
using System.Reflection;
using System.Linq;
using System.Web;
using System.Text.RegularExpressions;

namespace Microsoft.Bot.Builder.CognitiveServices.QnAMaker
{
    /// <summary>
    /// A dialog specialized to handle QnA response from QnA Maker.
    /// </summary>
    [Serializable]
    public class QnAMakerDialog : IDialog<IMessageActivity>
    {
        static string _membersName;
        static string _botName;

        protected readonly IQnAService[] services;
        private QnAMakerResults qnaMakerResults;
        private FeedbackRecord feedbackRecord;
        private const double QnAMakerHighConfidenceScoreThreshold = 0.99;
        private const double QnAMakerHighConfidenceDeltaThreshold = 0.20;

        //Added By Sumit
        //private string defaultMessage = "Hope that helped. If not, please feel free to rephrase your question or <a href='https://www.training.com' target='_blank'>talk to your mentor</a>.";
        private string defaultMessage = "Hope that helped. If not, please feel free to rephrase your question.";
        private string defaultMessageOnDone = "Nice speaking with you <<username>>. Hope you enjoyed the conversation. Feel free to contact me anytime.";

        public IQnAService[] MakeServicesFromAttributes()
        {
            var type = this.GetType();
            var qnaModels = type.GetCustomAttributes<QnAMakerAttribute>(inherit: true);
            return qnaModels.Select(m => new QnAMakerService(m)).Cast<IQnAService>().ToArray();
        }

        /// <summary>
        /// Construct the QnA Service dialog.
        /// </summary>
        /// <param name="services">The QnA service.</param>
        public QnAMakerDialog(params IQnAService[] services)
        {
            if (services.Length == 0)
            {
                services = MakeServicesFromAttributes();
            }

            SetField.NotNull(out this.services, nameof(services), services);
        }

        async Task IDialog<IMessageActivity>.StartAsync(IDialogContext context)
        {
            context.Wait(MessageReceivedAsync);
        }

        public async Task MessageReceivedAsync(IDialogContext context, IAwaitable<IMessageActivity> argument)
        {
            var message = await argument;
            var activity = (Activity)await argument;

            if (activity.Type == ActivityTypes.ConversationUpdate)
            {
                if (activity.MembersAdded != null && activity.MembersAdded.Any())
                {
                    string membersName = string.Join(
                        ", ",
                        activity.MembersAdded.Select(
                            newMember => (newMember.Id != activity.Recipient.Id) ? $"{newMember.Name}"
                                            : $"{activity.Recipient.Name}"));
                    
                    string membersID = string.Join(
                        ", ",
                        activity.MembersAdded.Select(
                            newMember => (newMember.Id != activity.Recipient.Id) ? $"{newMember.Id}"
                                            : $"{activity.Recipient.Id}"));
                    if (string.Compare(membersName, "KnowledgeGuru", true) == 0)
                    {
                        //_botName = membersName;
                        _botName = SetMentorName();
                        //await context.PostAsync($"Hello, I am {_botName}, your Digital Mentor. Please type your query and i would be happy to help. Type Done when you want to exit.");
                        await context.PostAsync($"Hello, I am {_botName}, your Student Services Executive. Please type your query and i would be happy to help. Type Thanks when you want to exit.");
                    }
                    else
                    {
                        _membersName = membersName;
                        //await context.PostAsync($"Welcome {membersName}.");
                    }
                    await this.DefaultWaitNextMessageAsync(context, message, qnaMakerResults);
                }
            }

            if (message.Type == ActivityTypes.Message)
            {
                if (message != null && !string.IsNullOrEmpty(message.Text))
                {

                   

                    if (string.Compare(message.Text, "Thanks", true) == 0 || string.Compare(message.Text, "Done", true) == 0  || string.Compare(message.Text, "Bye", true) == 0 || string.Compare(message.Text, "Close", true) == 0 || string.Compare(message.Text, "Fine", true) == 0)
                    {
                            await this.QnARatingStepAsync(context, qnaMakerResults);

                      

                    }
                    else
                    {
                        var tasks = this.services.Select(s => s.QueryServiceAsync(message.Text)).ToArray();
                        await Task.WhenAll(tasks);

                        if (tasks.Any())
                        {
                            var sendDefaultMessageAndWait = true;
                            qnaMakerResults = tasks.First(x => x.Result.ServiceCfg != null).Result;
                            if (tasks.Count(x => x.Result.Answers?.Count > 0) > 0)
                            {
                                var maxValue = tasks.Max(x => x.Result.Answers[0].Score);
                                qnaMakerResults = tasks.First(x => x.Result.Answers[0].Score == maxValue).Result;

                                if (qnaMakerResults != null && qnaMakerResults.Answers != null && qnaMakerResults.Answers.Count > 0)
                                {
                                    if (this.IsConfidentAnswer(qnaMakerResults))
                                    {
                                        await this.RespondFromQnAMakerResultAsync(context, message, qnaMakerResults);
                                        //Added By Sumit
                                        if (string.Compare(message.Text, "Hi", true) == 0)
                                        {
                                            await this.DefaultWaitNextMessageAsync(context, message, qnaMakerResults);
                                        }
                                        else 
                                        {
                                            await this.DefaultWaitNextMessageWithCustomMessageAsync(context, message, qnaMakerResults);
                                        }
                                        //Commented By Sumit
                                        //await this.DefaultWaitNextMessageAsync(context, message, qnaMakerResults);
                                    }
                                    else
                                    {
                                        feedbackRecord = new FeedbackRecord { UserId = message.From.Id, UserQuestion = message.Text };
                                        await this.QnAFeedbackStepAsync(context, qnaMakerResults);
                                    }

                                    sendDefaultMessageAndWait = false;
                                }
                            }

                            if (sendDefaultMessageAndWait)
                            {
                                await context.PostAsync(qnaMakerResults.ServiceCfg.DefaultMessage);
                                await this.DefaultWaitNextMessageAsync(context, message, qnaMakerResults);
                            }
                        }
                    }
                    
                }
            }

        }

        protected virtual bool IsConfidentAnswer(QnAMakerResults qnaMakerResults)
        {
            if (qnaMakerResults.Answers.Count <= 1 || qnaMakerResults.Answers.First().Score >= QnAMakerHighConfidenceScoreThreshold)
            {
                return true;
            }

            if (qnaMakerResults.Answers[0].Score - qnaMakerResults.Answers[1].Score > QnAMakerHighConfidenceDeltaThreshold)
            {
                return true;
            }

            return false;
        }

        private async Task ResumeAndPostAnswer(IDialogContext context, IAwaitable<string> argument)
        {
            try
            {
                var selection = await argument;
                if (qnaMakerResults != null)
                {
                    bool match = false;
                    foreach (var qnaMakerResult in qnaMakerResults.Answers)
                    {
                        if (qnaMakerResult.Questions[0].Equals(selection, StringComparison.OrdinalIgnoreCase))
                        {
                            await context.PostAsync(qnaMakerResult.Answer);

                            match = true;

                            if (feedbackRecord != null)
                            {
                                feedbackRecord.KbQuestion = qnaMakerResult.Questions.First();
                                feedbackRecord.KbAnswer = qnaMakerResult.Answer;

                                var tasks =
                                    this.services.Select(
                                        s =>
                                        s.ActiveLearnAsync(
                                            feedbackRecord.UserId,
                                            feedbackRecord.UserQuestion,
                                            feedbackRecord.KbQuestion,
                                            feedbackRecord.KbAnswer,
                                            qnaMakerResults.ServiceCfg.KnowledgebaseId)).ToArray();

                                await Task.WhenAll(tasks);
                                break;
                            }
                        }
                    }
                }
            }
            catch (TooManyAttemptsException) { }
            //Added By Sumit
            //await this.QnARatingStepAsync(context, qnaMakerResults);
            //Commented By Sumit
            await this.DefaultWaitNextMessageWithCustomMessageAsync(context, context.Activity.AsMessageActivity(), qnaMakerResults);
        }

        protected virtual async Task QnAFeedbackStepAsync(IDialogContext context, QnAMakerResults qnaMakerResults)
        {
            var qnaList = qnaMakerResults.Answers;
            var questions = qnaList.Select(x => x.Questions[0]).Concat(new[] { Resource.Resource.noneOfTheAboveOption }).ToArray();

            PromptOptions<string> promptOptions = new PromptOptions<string>(
                prompt: Resource.Resource.answerSelectionPrompt,
                tooManyAttempts: Resource.Resource.tooManyAttempts,
                options: questions,
                attempts: 0);

            PromptDialog.Choice(context: context, resume: ResumeAndPostAnswer, promptOptions: promptOptions);
        }

        protected virtual async Task RespondFromQnAMakerResultAsync(IDialogContext context, IMessageActivity message, QnAMakerResults result)
        {
            await context.PostAsync(result.Answers.First().Answer);
        }

        //Added by Sumit
        protected virtual async Task DefaultWaitNextMessageWithCustomMessageAsync(IDialogContext context, IMessageActivity message, QnAMakerResults result)
        {
            //Added By Sumit
            //await context.PostAsync(defaultMessage);
            context.Done(true);
        }

        //Added By Sumit
        protected virtual async Task DefaultRatingMessageAsync(IDialogContext context, IMessageActivity message, QnAMakerResults result)
        {
             //Added By Sumit
            await context.PostAsync(defaultMessageOnDone.Replace("<<username>>", _membersName));
            context.Done(true);
        }

        protected virtual async Task DefaultWaitNextMessageAsync(IDialogContext context, IMessageActivity message, QnAMakerResults result)
        {
            context.Done(true);
        }
        //Added By Sumit
        protected virtual async Task QnARatingStepAsync(IDialogContext context, QnAMakerResults qnaMakerResults)
        {
            //http://www.unicode.org/emoji/charts-beta/full-emoji-list.html#1f600
            //string[] ratingArray = { "Excellent", "Very Good", "Good", "Average", "Poor" };
            string[] ratingArray = { "\U0001F604", "\U0001F642", "\U0001F610", "\U0001F641", "\U0001F621" };
            var questions = ratingArray.ToArray();

            PromptOptions<string> promptOptions = new PromptOptions<string>(
                prompt: Resource.Resource.ratingSelectionPrompt,
                tooManyAttempts: Resource.Resource.tooManyAttempts,
                options: ratingArray,
                attempts: 0);
            PromptDialog.Choice(context: context, resume: ResumeAndPostFeedback, promptOptions: promptOptions);
        }

        //Added By Sumit
        private async Task ResumeAndPostFeedback(IDialogContext context, IAwaitable<string> argument)
        {
            await this.DefaultRatingMessageAsync(context, context.Activity.AsMessageActivity(), qnaMakerResults);
        }

        //Added By Sumit
        protected static IMessageActivity ProcessResultAndCreateMessageActivity(IDialogContext context, ref QnAMakerResult result)
        {
            var message = context.MakeMessage();

            //var attachmentsItemRegex = new Regex("((&lt;attachment){1}((?:\\s+)|(?:(contentType=&quot;[\\w\\/]+&quot;))(?:\\s+)|(?:(contentUrl=&quot;[\\w:/.]+&quot;))(?:\\s+)|(?:(name=&quot;[\\w\\s]+&quot;))(?:\\s+)|(?:(thumbnailUrl=&quot;[\\w:/.]+&quot;))(?:\\s+))+(/&gt;))", RegexOptions.IgnoreCase);
            var attachmentsItemRegex = new Regex("((&lt;attachment){1}((?:\\s+)|(?:(contentType=&quot;[\\w\\/-]+&quot;))(?:\\s+)|(?:(contentUrl=&quot;[\\w:/.=?-]+&quot;))(?:\\s+)|(?:(name=&quot;[\\w\\s&?\\-.@%$!£\\(\\)]+&quot;))(?:\\s+)|(?:(thumbnailUrl=&quot;[\\w:/.=?-]+&quot;))(?:\\s+))+(/&gt;))", RegexOptions.IgnoreCase);
            var matches = attachmentsItemRegex.Matches(result.Answer);

            foreach (var attachmentMatch in matches)
            {
                result.Answer = result.Answer.Replace(attachmentMatch.ToString(), string.Empty);

                var match = attachmentsItemRegex.Match(attachmentMatch.ToString());
                string contentType = string.Empty;
                string name = string.Empty;
                string contentUrl = string.Empty;
                string thumbnailUrl = string.Empty;

                foreach (var group in match.Groups)
                {
                    if (group.ToString().ToLower().Contains("contenttype="))
                    {
                        contentType = group.ToString().ToLower().Replace(@"contenttype=&quot;", string.Empty).Replace("&quot;", string.Empty);
                    }
                    if (group.ToString().ToLower().Contains("contenturl="))
                    {
                        contentUrl = group.ToString().ToLower().Replace(@"contenturl=&quot;", string.Empty).Replace("&quot;", string.Empty);
                    }
                    if (group.ToString().ToLower().Contains("name="))
                    {
                        name = group.ToString().ToLower().Replace(@"name=&quot;", string.Empty).Replace("&quot;", string.Empty);
                    }
                    if (group.ToString().ToLower().Contains("thumbnailurl="))
                    {
                        thumbnailUrl = group.ToString().ToLower().Replace(@"thumbnailurl=&quot;", string.Empty).Replace("&quot;", string.Empty);
                    }
                }

                var attachment = new Attachment(contentType, contentUrl, name: !string.IsNullOrEmpty(name) ? name : null, thumbnailUrl: !string.IsNullOrEmpty(thumbnailUrl) ? thumbnailUrl : null);
                message.Attachments.Add(attachment);
            }

            return message;
        }

        //Added by Sumit
        protected string SetMentorName()
        {
            var names = new List<string> { "Neha", "Adela", "Ginni", "Roop", "Shalini" };
            int index = new Random().Next(names.Count);
            var name = names[index];
            return name;
        }
    }
}
