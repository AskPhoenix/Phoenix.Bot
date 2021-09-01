﻿using AdaptiveCards;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Newtonsoft.Json.Linq;
using Phoenix.Bot.Dialogs.Actions.Preparation;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.AdaptiveCards;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Miscellaneous;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.DataHandle.Main;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class BroadcastDialog : ComponentDialog
    {
        private readonly AspNetUserRepository userRepository;

        public BroadcastDialog(PhoenixContext phoenixContext)
            : base(nameof(BroadcastDialog))
        {
            this.userRepository = new AspNetUserRepository(phoenixContext);

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new TextPrompt(nameof(TextPrompt)));
            AddDialog(new DateTimePrompt(nameof(DateTimePrompt), PromptValidators.CustomDateTimePromptValidator, "fr-fr"));

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Broadcast.Top,
                new WaterfallStep[]
                {
                    PrepareStepAsync,
                    ConfirmStepAsync,
                    SetupStepAsync,
                    RepeatStepAsync,
                    EndStepAsync
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

        private async Task<DialogTurnResult> PrepareStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.BeginDialogAsync(WaterfallNames.Actions.Broadcast.Preparation, stepContext.Options, cancellationToken);
        }

        private async Task<DialogTurnResult> ConfirmStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var broadcast = stepContext.Result as Broadcast;

            var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 2))
            {
                BackgroundImage = new AdaptiveBackgroundImage(AdaptiveCardsHelper.DarkBackgroundImageUrl)
            };

            card.Body.Add(new AdaptiveTextBlockHeaderLight("Νέα Ανακοίνωση"));
            card.Body.Add(new AdaptiveRichFactSetLight("Περιεχόμενο ", broadcast.Message));
            card.Body.Add(new AdaptiveRichFactSetLight("Ημερομηνία ", 
                broadcast.ScheduledDate.ToString("dd/MM") + " - " + 
                broadcast.Daypart.ToFriendlyString(), separator: true));
            card.Body.Add(new AdaptiveRichFactSetLight("Κοινό ", broadcast.Audience.ToFriendlyString(), separator: true));

            string visibilityText = broadcast.Visibility.ToFriendlyString();
            if (broadcast.Visibility == BroadcastVisibility.Group)
            {
                //TODO: Find group name
                visibilityText += " " + "";
            }
            card.Body.Add(new AdaptiveRichFactSetLight("Ορατότητα ", visibilityText, separator: true));

            Attachment attachment = new(contentType: AdaptiveCard.ContentType, content: JObject.FromObject(card));

            await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(attachment));

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Οι παραπάνω πληροφορίες είναι σωστές;", simpleNo: true),
                cancellationToken);
        }

        private async Task<DialogTurnResult> SetupStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            bool confirmed = (stepContext.Result as FoundChoice).Index == 0;
            if (!confirmed)
            {
                await stepContext.Context.SendActivityAsync("Ας ξαναπροσπαθήσουμε!");

                return await stepContext.ReplaceDialogAsync(WaterfallNames.Actions.Broadcast.Top, stepContext.Options, cancellationToken);
            }

            //TODO: Save Broadcast on the database

            await stepContext.Context.SendActivityAsync("Η ανακοίνωση καταχωρήθηκε επιτυχώς!");

            return await stepContext.NextAsync(cancellationToken: cancellationToken);
        }

        private async Task<DialogTurnResult> RepeatStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Θα ήθελες να δημιουργήσεις άλλη ανακοίνωση;"),
                cancellationToken);
        }

        private async Task<DialogTurnResult> EndStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            await stepContext.Context.SendActivityAsync("Έγινε!");

            if ((stepContext.Result as FoundChoice).Index == 0)
                return await stepContext.ReplaceDialogAsync(WaterfallNames.Actions.Broadcast.Top, stepContext.Options, cancellationToken);

            return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
        }

        #endregion

        #region Preparation Waterfall Dialog

        private async Task<DialogTurnResult> InitialStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            stepContext.Values.Add(nameof(Broadcast), new Broadcast());

            return await stepContext.NextAsync(cancellationToken: cancellationToken);
        }

        private async Task<DialogTurnResult> MessageAskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.PromptAsync(
                nameof(TextPrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Πληκτρολόγησε το περιεχόμενο της ανακοίνωσης παρακάτω:")
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> MessageGetStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var broadcast = (Broadcast)stepContext.Values[nameof(Broadcast)];
            broadcast.Message = (string)stepContext.Result;

            await stepContext.Context.SendActivityAsync("Μήνυμα ελήφθη!");

            return await stepContext.NextAsync(cancellationToken: cancellationToken);
        }

        private async Task<DialogTurnResult> DateAskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var choices = new string[2] { "Σήμερα", "Αύριο" };
            await stepContext.Context.SendActivityAsync("Πότε θα ήθελες να σταλεί η ανακοίνωση;");
            var prompt = ChoiceFactory.SuggestedAction(choices, text: "Μπορείς επίσης να συμπληρώσεις μία ημερομηνία στη μορφή ηη/μμ:");
            var repropmt = ChoiceFactory.SuggestedAction(choices, text: "Η επιθυμητή ημερομηνία θα πρέπει να είναι στη μορφή ηη/μμ:");

            return await stepContext.PromptAsync(
                nameof(DateTimePrompt),
                new PromptOptions
                {
                    Prompt = (Activity)prompt,
                    RetryPrompt = (Activity)repropmt
                });
        }

        private async Task<DialogTurnResult> DateGetStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            //TODO: Accept only future dates

            var broadcast = (Broadcast)stepContext.Values[nameof(Broadcast)];

            var msg = stepContext.Context.Activity.Text;
            var res = stepContext.Result as IList<DateTimeResolution>;

            broadcast.ScheduledDate = CalendarExtensions.ResolveDateTimePromptResult(res, msg);

            return await stepContext.NextAsync(cancellationToken: cancellationToken);
        }

        private async Task<DialogTurnResult> DaypartAskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            //TODO: Exclude passed dayparts if the selected date is today
            //TODO: Add "now" choice

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Πότε μέσα στην ημέρα θα ήθελες να σταλεί η ανακοίνωση;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ένα από τα παρακάτω:"),
                    Choices = ChoiceFactory.ToChoices(Enum.GetValues(typeof(Daypart)).Cast<Daypart>().
                        Select(dp => dp.ToFriendlyString()).ToArray())
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> DaypartGetStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var broadcast = (Broadcast)stepContext.Values[nameof(Broadcast)];
            broadcast.Daypart = (Daypart)(stepContext.Result as FoundChoice).Index;

            return await stepContext.NextAsync(cancellationToken: cancellationToken);
        }

        private async Task<DialogTurnResult> AudienceAskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Σε ποιον απευθύνεται η ανακοίνωση;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε μία από τις παρακάτω ομάδες:"),
                    Choices = ChoiceFactory.ToChoices(Enum.GetValues(typeof(BroadcastAudience)).Cast<BroadcastAudience>().
                        Select(ba => ba.ToFriendlyString()).ToArray())
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> AudienceGetStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var broadcast = (Broadcast)stepContext.Values[nameof(Broadcast)];
            broadcast.Audience = (BroadcastAudience)(stepContext.Result as FoundChoice).Index;

            await stepContext.Context.SendActivityAsync("ΟΚ! Και κάτι ακόμη...");

            return await stepContext.NextAsync(cancellationToken: cancellationToken);
        }

        private async Task<DialogTurnResult> VisibilityAskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Η ανακοίνωση αφορά κάποιο συγκεκριμένο τμήμα;", simpleNo: true),
                cancellationToken);
        }

        private async Task<DialogTurnResult> VisibilityGetStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var broadcast = (Broadcast)stepContext.Values[nameof(Broadcast)];
            broadcast.Visibility = (BroadcastVisibility)(stepContext.Result as FoundChoice).Index;

            if (broadcast.Visibility == BroadcastVisibility.Group)
                return await stepContext.NextAsync(cancellationToken: cancellationToken);

            return await stepContext.EndDialogAsync(broadcast, cancellationToken);
        }

        private async Task<DialogTurnResult> GroupAskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var broadcast = (Broadcast)stepContext.Values[nameof(Broadcast)];
            var preparationOptions = new PreparationOptions(
                new[] { BotActionPreparation.GroupSelection }, stepContext.Options as ActionOptions);

            return await stepContext.BeginDialogAsync(nameof(PreparationDialog), preparationOptions, cancellationToken);
        }

        private async Task<DialogTurnResult> GroupGetStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var broadcast = (Broadcast)stepContext.Values[nameof(Broadcast)];
            var actionOptions = stepContext.Result as ActionOptions;

            broadcast.CourseId = actionOptions.CourseId;

            return await stepContext.EndDialogAsync(broadcast, cancellationToken);
        }

        #endregion
    }
}
