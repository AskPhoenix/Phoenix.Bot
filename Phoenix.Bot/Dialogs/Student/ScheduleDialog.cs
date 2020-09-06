using AdaptiveCards;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Phoenix.Bot.Extensions;
using Phoenix.Bot.Helpers;
using Phoenix.DataHandle.Main;
using Phoenix.DataHandle.Main.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Phoenix.Bot.Helpers.CardHelper;

namespace Phoenix.Bot.Dialogs.Student
{
    public class ScheduleDialog : ComponentDialog
    {
        private readonly PhoenixContext _phoenixContext;

        private static class WaterfallNames
        {
            public const string Day = "StudentSchedule_Day_WaterfallDialog";
            public const string Week = "StudentSchedule_Week_WaterfallDialog";
        }

        public ScheduleDialog(PhoenixContext phoenixContext)
            : base(nameof(ScheduleDialog))
        {
            _phoenixContext = phoenixContext;

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new DateTimePrompt(nameof(DateTimePrompt)));

            AddDialog(new WaterfallDialog(WaterfallNames.Day,
                new WaterfallStep[]
                {
                    DayStepAsync,
                    DayOtherRedirectStepAsync,
                    DayOtherStepAsync,
                    DayResolveStepAsync,
                    SpecificDateStepAsync,
                    SpecificDateSelectStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Week,
                new WaterfallStep[]
                {
                    WeekStepAsync,
                    WeekDayMoreStepAsync
                }));

            InitialDialogId = WaterfallNames.Day;
        }

        #region Day Waterfall Dialog

        private async Task<DialogTurnResult> DayStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            DateTime date = stepContext.Options is DateTime dt ? dt : DialogHelper.GreeceLocalTime();
            string userFbId = stepContext.Context.Activity.From.Id;
            string schoolFbId = stepContext.Context.Activity.Recipient.Id;

            var lecs = _phoenixContext.Lecture.
                Include(l => l.Course).
                Include(l => l.Classroom).
                Where(l => l.Course.StudentCourse.Any(sc => sc.Student.AspNetUser.FacebookId == userFbId) && l.Course.School.FacebookPageId == schoolFbId
                    && l.StartDateTime.Date == date.Date).
                OrderBy(l => l.StartDateTime);

            if (lecs.Count() == 0)
            {
                int dayOffset = (DialogHelper.GreeceLocalTime() - date).Days;
                string dayName = dayOffset switch
                {
                    0 => "σήμερα",
                    1 => "αύριο",
                    2 => "μεθάυριο",
                    var o when o >= 3 && o <= 7 => $"την επόμενη {date:dddd}",
                    _ => $"τις {date.Day}/{date.Month}"
                };

                await stepContext.Context.SendActivityAsync($"Δεν {(dayOffset >=0 ? "έχεις" : "είχες")} μαθήματα για {dayName}! 😎");
            }
            else
            {
                await stepContext.Context.SendActivityAsync(new Activity(type: ActivityTypes.Typing));

                var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 2));
                card.BackgroundImage = new AdaptiveBackgroundImage("https://www.bot.askphoenix.gr/assets/4f5d75_sq.png");
                card.Body.Add(new AdaptiveTextBlockHeaderLight($"Πρόγραμμα - {date:dddd} {date.Day}/{date.Month}"));

                foreach (var lec in lecs)
                {
                    card.Body.Add(new AdaptiveTextBlockHeaderLight(lec.Course.Name + (lec.Course.SubCourse != null ? $" - {lec.Course.SubCourse}" : "")));
                    card.Body.Add(new AdaptiveRichFactSetLight("Ώρες ", $"{lec.StartDateTime:t} - {lec.EndDateTime:t}"));
                    card.Body.Add(new AdaptiveRichFactSetLight("Αίθουσα ", lec.Classroom.Name, separator: true));
                    card.Body.Add(new AdaptiveRichFactSetLight("Κατάσταση ", lec.Status.ToGreekString(), separator: true));
                    card.Body.Add(new AdaptiveRichFactSetLight("Σχόλια ", string.IsNullOrEmpty(lec.Info) ? "-" : lec.Info, separator: true));
                }

                await stepContext.Context.SendActivityAsync(
                        MessageFactory.Attachment(new Attachment(contentType: AdaptiveCard.ContentType, content: JObject.FromObject(card))));
            }

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να δεις το πρόγραμμα για άλλη ημέρα ή για ολόκληρη την εβδομάδα;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε μία από τις παρακάτω απαντήσεις:"),
                    Choices = new Choice[] { new Choice("🌅 Άλλη ημέρα"), new Choice("🗓 Εβδομαδιαίο"), new Choice("❌ Όχι, ευχαριστώ") { Synonyms = new List<string> { "Όχι" } } }
                });
        }

        private async Task<DialogTurnResult> DayOtherRedirectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 2)
            {
                await stepContext.Context.SendActivityAsync("OK 😊");
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }
            if (foundChoice.Index == 1)
                return await stepContext.BeginDialogAsync(WaterfallNames.Week, null, cancellationToken);

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> DayOtherStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var grNow = DialogHelper.GreeceLocalTime();
            var choices = new List<string>(8);
            for (int i = 1; i <= 7; i++)
            {
                DateTime nextDay = grNow.AddDays(i);
                choices.Add($"{nextDay:dddd} - {nextDay.Day}/{nextDay.Month}");
            }
            choices.Add("📅 Άλλη ημερομηνία");

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Για ποια μέρα θα ήθελες να δεις το πρόγραμμά σου;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ χρησιμοποίησε μία από τις παρακάτω επιλογές:"),
                    Choices = ChoiceFactory.ToChoices(choices)
                });
        }

        private async Task<DialogTurnResult> DayResolveStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            int foundChoiceIndex = (stepContext.Result as FoundChoice).Index;
            if (foundChoiceIndex == 7)
                return await stepContext.NextAsync(null, cancellationToken);

            DateTime date = DialogHelper.GreeceLocalTime().AddDays(foundChoiceIndex + 1);
            return await stepContext.ReplaceDialogAsync(WaterfallNames.Day, date, cancellationToken);
        }

        private async Task<DialogTurnResult> SpecificDateStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.PromptAsync(
                nameof(DateTimePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Γράψε μια ημερομηνία στη μορφή ημέρα/μήνας, ώστε να δεις το πρόγραμμα για εκείνη τη μέρα:"),
                    RetryPrompt = MessageFactory.Text("Η επιθυμητή ημερομηνία θα πρέπει να είναι στη μορφή ημέρα/μήνας (π.χ. 24/4)):")
                });

        private async Task<DialogTurnResult> SpecificDateSelectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var selDate = DialogHelper.ResolveDateTime(stepContext.Result as IList<DateTimeResolution>);
            return await stepContext.ReplaceDialogAsync(WaterfallNames.Day, selDate, cancellationToken);
        }

        #endregion

        #region Week Waterfall Dialog

        private async Task<DialogTurnResult> WeekStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var grNow = DialogHelper.GreeceLocalTime();
            string userFbId = stepContext.Context.Activity.From.Id;
            string schoolFbId = stepContext.Context.Activity.Recipient.Id;

            var lecs = _phoenixContext.Lecture.
                Include(l => l.Course).
                Where(l => l.Course.StudentCourse.Any(sc => sc.Student.AspNetUser.FacebookId == userFbId) && l.Course.School.FacebookPageId == schoolFbId
                    && l.StartDateTime.Date > grNow.Date && l.StartDateTime.Date <= grNow.AddDays(7).Date).
                OrderBy(l => l.StartDateTime).
                AsEnumerable();

            if (lecs.Count() == 0)
            {
                await stepContext.Context.SendActivityAsync($"Δεν έχεις μαθήματα για τις επόμενες 7 ημέρες! 😎");
            }
            else
            {
                await stepContext.Context.SendActivityAsync(new Activity(type: ActivityTypes.Typing));

                var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 2));
                card.BackgroundImage = new AdaptiveBackgroundImage("https://www.bot.askphoenix.gr/assets/4f5d75_sq.png");
                card.Body.Add(new AdaptiveTextBlockHeaderLight("Εβδομαδιαίο Πρόγραμμα"));
                card.Body.Add(new AdaptiveTextBlockHeaderLight($"{grNow.Day}/{grNow.Month} έως {grNow.AddDays(7).Day}/{grNow.AddDays(7).Month}"));
             
                for (int i = 1; i <= 7; i++)
                {
                    DateTime nextDay = grNow.AddDays(i);
                    card.Body.Add(new AdaptiveTextBlockHeaderLight(nextDay.ToString("dddd")));
                    if (lecs.Any(l => l.StartDateTime.DayOfWeek == nextDay.DayOfWeek))
                    {
                        var dayLecs = lecs.Where(l => l.StartDateTime.DayOfWeek == nextDay.DayOfWeek);
                        foreach (var lec in dayLecs)
                        {
                            card.Body.Add(new AdaptiveRichFactSetLight("Μάθημα ", lec.Course.Name + (lec.Course.SubCourse != null ? $" - {lec.Course.SubCourse}" : "")));
                            card.Body.Add(new AdaptiveRichFactSetLight("Ώρες ", $"{lec.StartDateTime:t} - {lec.EndDateTime:t}", separator: true));
                            card.Body.Add(new AdaptiveRichFactSetLight());
                        }
                    }
                    else
                        card.Body.Add(new AdaptiveTextBlockHeaderLight("-"));
                }

                await stepContext.Context.SendActivityAsync(
                        MessageFactory.Attachment(new Attachment(contentType: AdaptiveCard.ContentType, content: JObject.FromObject(card))));
            }

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να δεις το πρόγραμμα για άλλη ημέρα;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ απάντησε με ένα Ναι ή Όχι:"),
                    Choices = new Choice[] { new Choice("✔️ Ναι"), new Choice("❌ Όχι, ευχαριστώ") { Synonyms = new List<string> { "Όχι" } } }
                });
        }

        private async Task<DialogTurnResult> WeekDayMoreStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
                return await stepContext.EndDialogAsync(null, cancellationToken);

            await stepContext.Context.SendActivityAsync("OK 😊");
            return await stepContext.Parent.EndDialogAsync(null, cancellationToken);
        }

        #endregion
    }
}
