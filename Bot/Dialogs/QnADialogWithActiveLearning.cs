using System;
using Microsoft.Bot.Builder.CognitiveServices.QnAMaker;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace QnABotCustom.Dialogs
{
    [Serializable]
    //[QnAMaker("set your subscription key here", "set your kbid here", "I don't understand this right now! Try another query!", 0.50, 3)]
    //OOPS BOT
    //[QnAMaker("509b3f29f32e493b954cd8d608c127ab", "b9f808c1-6d19-409c-a214-b0123b11cf81", "Sorry, I don't understand your question. Feel free to rephrase your question.", 0.50, 3)]
    //FAQ BOT
    //[QnAMaker("509b3f29f32e493b954cd8d608c127ab", "dd5360f0-6723-4c86-8e99-87babf2c9651", "Sorry, I don't understand your question. Feel free to rephrase your question.", 0.50, 3)]
    //DM BOT
    [QnAMaker("EndpointKey eedbcd8d-c7c9-439f-b494-ad7f00b7dd47", "8f0d05f1-a7c5-4cff-b70a-e2006ea06687", "Sorry, I don't understand your question. Feel free to rephrase your question or contact Student Service Admin of your center.", 0.50, 3)]
    public class QnADialogWithActiveLearning : QnAMakerDialog
    {

    }
}