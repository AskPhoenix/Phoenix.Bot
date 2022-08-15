using AdaptiveCards;
using Microsoft.Bot.Schema;
using Phoenix.Bot.Dialogs.Actions.Preparation;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.AdaptiveCards;
using Phoenix.Bot.Utilities.Linguistic;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.Bot.Utilities.State.Options.Actions.Preparation;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class BroadcastDialog : StateDialog
    {
        private readonly CourseRepository _courseRepository;
        private readonly BroadcastRepository _broadcastRepository;

        private static readonly HttpClient httpClient = new();
        private const string ProactiveBotUrl = "https://proactive.bot.askphoenix.gr/";

        public BroadcastDialog(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext)
            : base(userState, convState, userManager, phoenixContext, nameof(BroadcastDialog))
        {
            _courseRepository = new(phoenixContext, nonObviatedOnly: true);
            _broadcastRepository = new(phoenixContext);

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new TextPrompt(nameof(TextPrompt)));
            AddDialog(new DateTimePrompt(nameof(DateTimePrompt), PromptValidators.FutureDateTimePromptValidator, "fr-FR"));

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Broadcast.Top,
                new WaterfallStep[]
                {
                    PrepareStepAsync,
                    ConfirmStepAsync,
                    SetupStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Broadcast.Preparation,
                new WaterfallStep[]
                {
                    InitialStepAsync,
                    MessageAskStepAsync,
                    MessageGetStepAsync,
                    DateAskStepAsync,
                    DateGetStepAsync,
                    DaypartAskStepAsync,
                    DaypartGetStepAsync,
                    AudienceAskStepAsync,
                    AudienceGetStepAsync,
                    VisibilityAskStepAsync,
                    VisibilityGetStepAsync,
                    GroupAskStepAsync,
                    GroupGetStepAsync
                }));

            InitialDialogId = WaterfallNames.Actions.Broadcast.Top;
        }

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> PrepareStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            return await stepCtx.BeginDialogAsync(WaterfallNames.Actions.Broadcast.Preparation,
                stepCtx.Options, canTkn);
        }

        private async Task<DialogTurnResult> ConfirmStepAsync(WaterfallStepContext stepCtx, CancellationToken canTkn)
        {
            var broadcast = (Broadcast)stepCtx.Result;
            stepCtx.Values.Add(nameof(Broadcast), broadcast);

            var card = new AdaptivePhoenixCard(new AdaptiveElement[]
            {
                new AdaptiveTextBlockHeaderLight("Νέα Ανακοίνωση"),
                new AdaptiveRichFactSetLight("Περιεχόμενο ", broadcast.Message),
                new AdaptiveRichFactSetLight("Ημερομηνία ", broadcast.ScheduledFor.ToString("dd/MM") + " - " +
                    broadcast.Daypart.ToFriendlyString(), separator: true),
                new AdaptiveRichFactSetLight("Κοινό ", broadcast.Audience.ToFriendlyString(), separator: true)
            });
            
            string visibilityText = broadcast.Visibility.ToFriendlyString();
            if (broadcast.Visibility == BroadcastVisibility.Group)
            {
                var courseFullNames = broadcast.Courses.Select(c => c.GetFullName());
                visibilityText += string.Join(", ", courseFullNames);
            }

            card.Body.Add(new AdaptiveRichFactSetLight("Ορατότητα ", visibilityText, separator: true));

            await stepCtx.Context.SendActivityAsync(card.ToActivity());

            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Οι παραπάνω πληροφορίες είναι σωστές;", simpleNo: true),
                canTkn);
        }

        private async Task<DialogTurnResult> SetupStepAsync(WaterfallStepContext stepCtx, CancellationToken canTkn)
        {
            var foundChoice = (FoundChoice)stepCtx.Result;
            bool confirmed = foundChoice.Index == 0;

            if (!confirmed)
            {
                await stepCtx.Context.SendActivityAsync("Ας ξαναπροσπαθήσουμε!");

                return await stepCtx.ReplaceDialogAsync(WaterfallNames.Actions.Broadcast.Top,
                    stepCtx.Options, canTkn);
            }

            var broadcast = (Broadcast)stepCtx.Values[nameof(Broadcast)];

            broadcast.School = CData.School;
            broadcast.Author = UData.PhoenixUser;
            broadcast.Status = BroadcastStatus.Pending;

            await _broadcastRepository.CreateAsync(broadcast);

            if (broadcast.Daypart == Daypart.Now)
            {
                //TODO: Create a BroadcastWrapper
                await httpClient.PostAsync(ProactiveBotUrl + $"broadcast/id/{broadcast.Id}", null);
                await stepCtx.Context.SendActivityAsync("Η ανακοίνωση εστάλη επιτυχώς!");
            }
            else
            {
                await stepCtx.Context.SendActivityAsync("Η ανακοίνωση καταχωρήθηκε επιτυχώς!");
            }

            return await stepCtx.EndDialogAsync(null, canTkn);
        }

        #endregion

        #region Preparation Waterfall Dialog

        private async Task<DialogTurnResult> InitialStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            stepCtx.Values.Add(nameof(Broadcast), new Broadcast());

            return await stepCtx.NextAsync(null, canTkn);
        }

        private async Task<DialogTurnResult> MessageAskStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            return await stepCtx.PromptAsync(
                nameof(TextPrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Πληκτρολόγησε το περιεχόμενο της ανακοίνωσης παρακάτω:")
                },
                canTkn);
        }

        private async Task<DialogTurnResult> MessageGetStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var broadcast = (Broadcast)stepCtx.Values[nameof(Broadcast)];
            broadcast.Message = (string)stepCtx.Result;

            await stepCtx.Context.SendActivityAsync("Μήνυμα ελήφθη!");

            return await stepCtx.NextAsync(null, canTkn);
        }

        private async Task<DialogTurnResult> DateAskStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var choices = new List<string>(5)
            {
                DateLiteral.Today.ToFriendlyString(),
                DateLiteral.Tomorrow.ToFriendlyString()
            };
            
            var today = CalendarExtensions.TimeZoneNow(CData.School.SchoolSetting.TimeZone).Date;
            var dates = new DateTimeOffset[] { today.AddDays(2.0), today.AddDays(3.0), today.AddDays(4.0) };

            var sel = PreparationComponent.GetSelectables(dates).Select(s => s.Value);
            choices.AddRange(sel);
            
            var prompt = ChoiceFactory.SuggestedAction(choices, text: "Πότε θα ήθελες να σταλεί η ανακοίνωση;");
            var repropmt = ChoiceFactory.SuggestedAction(choices, text: "Η επιθυμητή ημερομηνία θα πρέπει να μην έχει περάσει και να είναι στη μορφή ηη/μμ:");

            return await stepCtx.PromptAsync(
                nameof(DateTimePrompt),
                new PromptOptions
                {
                    Prompt = (Activity)prompt,
                    RetryPrompt = (Activity)repropmt
                }, canTkn);
        }

        private async Task<DialogTurnResult> DateGetStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var broadcast = (Broadcast)stepCtx.Values[nameof(Broadcast)];

            var msg = stepCtx.Context.Activity.Text;
            var res = (IList<DateTimeResolution>)stepCtx.Result;

            var offset = CalendarExtensions.CalculateTimeZoneOffset(
                CData.School.SchoolSetting.TimeZone, DateTime.UtcNow);
            
            broadcast.ScheduledFor = ResolveHelper.ResolveDateTimePromptResult(res, msg).ToOffset(offset).Date;

            return await stepCtx.NextAsync(null, canTkn);
        }

        private async Task<DialogTurnResult> DaypartAskStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var now = CalendarExtensions.TimeZoneNow(CData.School.SchoolSetting.TimeZone);
            var choices = DaypartExtensions.FindFutureDayparts(now).Select(dp => dp.ToFriendlyString()).ToList();

            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Πότε μέσα στην ημέρα θα ήθελες να σταλεί η ανακοίνωση;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ένα από τα παρακάτω:"),
                    Choices = ChoiceFactory.ToChoices(choices)
                }, canTkn);
        }

        private async Task<DialogTurnResult> DaypartGetStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var broadcast = (Broadcast)stepCtx.Values[nameof(Broadcast)];
            var foundChoice = (FoundChoice)stepCtx.Result;

            broadcast.Daypart = (Daypart)(foundChoice.Index + 1);

            return await stepCtx.NextAsync(null, canTkn);
        }

        private async Task<DialogTurnResult> AudienceAskStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Σε ποιον απευθύνεται η ανακοίνωση;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε μία από τις παρακάτω ομάδες:"),
                    Choices = ChoiceFactory.ToChoices(BroadcastAudienceExtensions.GetFriendlyStrings())
                }, canTkn);
        }

        private async Task<DialogTurnResult> AudienceGetStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var broadcast = (Broadcast)stepCtx.Values[nameof(Broadcast)];
            var foundChoice = (FoundChoice)stepCtx.Result;

            broadcast.Audience = (BroadcastAudience)(foundChoice.Index + 1);

            await stepCtx.Context.SendActivityAsync("ΟΚ! Και κάτι ακόμη...");

            return await stepCtx.NextAsync(null, canTkn);
        }

        private async Task<DialogTurnResult> VisibilityAskStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Η ανακοίνωση αφορά κάποιο συγκεκριμένο τμήμα;", simpleNo: true),
                canTkn);
        }

        private async Task<DialogTurnResult> VisibilityGetStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var broadcast = (Broadcast)stepCtx.Values[nameof(Broadcast)];
            var foundChoice = (FoundChoice)stepCtx.Result;

            broadcast.Visibility = (BroadcastVisibility)(foundChoice.Index + 1);

            if (broadcast.Visibility == BroadcastVisibility.Group)
                return await stepCtx.NextAsync(null, canTkn);

            return await stepCtx.EndDialogAsync(broadcast, canTkn);
        }

        private async Task<DialogTurnResult> GroupAskStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var broadcast = (Broadcast)stepCtx.Values[nameof(Broadcast)];

            var preparationOptions = new PreparationOptions((ActionOptions)stepCtx.Options,
                new[] { BotActionPreparation.GroupSelection });

            return await stepCtx.BeginDialogAsync(nameof(PreparationDialog), preparationOptions, canTkn);
        }

        private async Task<DialogTurnResult> GroupGetStepAsync(WaterfallStepContext stepCtx, CancellationToken canTkn)
        {
            var broadcast = (Broadcast)stepCtx.Values[nameof(Broadcast)];
            var actionOptions = (ActionOptions)stepCtx.Result;

            var course = await _courseRepository.FindPrimaryAsync(actionOptions.CourseId!.Value, canTkn);
            if (course is null)
                throw new BotException(BotError.CourseNotValid);

            broadcast.Courses.Add(course);

            return await stepCtx.EndDialogAsync(broadcast, canTkn);
        }

        #endregion
    }
}
