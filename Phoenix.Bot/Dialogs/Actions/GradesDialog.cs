using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main.Models;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class GradesDialog : StateDialog
    {
        public GradesDialog(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext)
            : base(userState, convState, userManager, phoenixContext, nameof(BroadcastDialog))
        {
            throw new NotImplementedException();

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Grades.Top,
                new WaterfallStep[]
                {
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Grades.Recent,
                new WaterfallStep[]
                {

                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Grades.Search,
                new WaterfallStep[]
                {

                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Grades.Mark,
                new WaterfallStep[]
                {

                }));

            InitialDialogId = WaterfallNames.Actions.Grades.Top;
        }
    }
}
