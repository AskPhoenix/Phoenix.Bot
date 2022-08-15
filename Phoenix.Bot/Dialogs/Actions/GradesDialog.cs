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
