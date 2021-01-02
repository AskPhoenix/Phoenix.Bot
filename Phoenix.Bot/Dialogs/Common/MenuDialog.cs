using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Phoenix.Bot.Dialogs.Common.Authentication;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Common
{
    public class MenuDialog : ComponentDialog
    {
        public MenuDialog()
            : base(nameof(MenuDialog))
        {
            AddDialog(new WaterfallDialog(WaterfallNames.Introduction.Top,
                new WaterfallStep[]
                {
                    
                }));

            InitialDialogId = WaterfallNames.Introduction.Top;
        }
    }
}
