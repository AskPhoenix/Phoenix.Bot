﻿using AdaptiveCards;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Newtonsoft.Json.Linq;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.AdaptiveCards;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.DataHandle.Main;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class ScheduleDialog : ComponentDialog
    {
        private readonly CourseRepository courseRepository;
        private readonly LectureRepository lectureRepository;

        public ScheduleDialog(PhoenixContext phoenixContext)
            : base(nameof(ScheduleDialog))
        {
            this.courseRepository = new(phoenixContext);
            this.lectureRepository = new(phoenixContext);
            this.lectureRepository.Include(l => l.Course);
            this.lectureRepository.Include(l => l.Classroom);

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new DateTimePrompt(nameof(DateTimePrompt)));


            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Schedule.Weekly,
                new WaterfallStep[]
                {
                    WeeklyStepAsync,
                    OtherDayStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Schedule.Daily,
                new WaterfallStep[]
                {
                    DailyStepAsync,
                    OtherDayStepAsync,
                }));

            InitialDialogId = WaterfallNames.Actions.Schedule.Weekly;
        }

        protected override Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options, CancellationToken cancellationToken = default)
        {
            var scheduleOptions = options as ScheduleOptions;
            if (scheduleOptions.Daily)
                InitialDialogId = WaterfallNames.Actions.Schedule.Daily;

            return base.OnBeginDialogAsync(innerDc, options, cancellationToken);
        }



        #region Weekly Waterfall Dialog

        private async Task<DialogTurnResult> WeeklyStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var scheduleOptions = stepContext.Options as ScheduleOptions;
            int[] courseIds = courseRepository.FindForUser(scheduleOptions.ActiveUserId, scheduleOptions.UserRole.IsStaff()).Select(c => c.Id).ToArray();
            DateTime closestDate = lectureRepository.FindClosestLectureDates(courseIds, Tense.Future, dayRange: 1).SingleOrDefault();
            
            //This error occurs only when there are no Lectures, which should not be the case.
            //Lectures are created when a School is enrolled and are updated regularly afterwards.
            if (closestDate == default)
            {
                await stepContext.Context.SendActivityAsync("Δεν έχουν προγραμματιστεί ακόμα τα μαθήματα.");
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

            DateTime monday = closestDate.AddDays(-(int)closestDate.DayOfWeek);

            var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 2))
            {
                BackgroundImage = new AdaptiveBackgroundImage(AdaptiveCardsHelper.DarkBackgroundImageUrl)
            };
            card.Body.Add(new AdaptiveTextBlockHeaderLight("Εβδομαδιαίο πρόγραμμα"));
            card.Body.Add(new AdaptiveTextBlockHeaderLight($"{monday:d/M} έως {monday.AddDays(6):d/M}"));

            for (int i = 0; i < 6; i++)
            {
                DateTime nextDay = monday.AddDays(i);
                var dayLectures = lectureRepository.FindMany(courseIds, nextDay);

                if (dayLectures.Any())
                {
                    card.Body.Add(new AdaptiveTextBlockHeaderLight(nextDay.ToString("dddd")));
                    foreach (var lec in dayLectures)
                    {
                        card.Body.Add(new AdaptiveRichFactSetLight("Μάθημα ", lec.Course.NameWithSubcourse));
                        card.Body.Add(new AdaptiveRichFactSetLight("Ώρες ", $"{lec.StartDateTime:t} - {lec.EndDateTime:t}", separator: true));
                        card.Body.Add(new AdaptiveRichFactSetLight());
                    }
                }
            }

            Attachment attachment = new(contentType: AdaptiveCard.ContentType, content: JObject.FromObject(card));
            await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(attachment));

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt), new YesNoPromptOptions("Θα ήθελες να δεις το πρόγραμμα για άλλη ημέρα;"));
        }

        private async Task<DialogTurnResult> OtherDayStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
                return await stepContext.EndDialogAsync(BotAction.ScheduleDaily, cancellationToken);

            await stepContext.Context.SendActivityAsync("OK 😊");
            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion

        #region Daily Waterfall Dialog

        private async Task<DialogTurnResult> DailyStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var scheduleOptions = stepContext.Options as ScheduleOptions;
            DateTime date = scheduleOptions.DateToPrepareFor.Value.Date;
            int[] courseIds = courseRepository.FindForUser(scheduleOptions.ActiveUserId, scheduleOptions.UserRole.IsStaff()).Select(c => c.Id).ToArray();
            var lectures = lectureRepository.FindMany(courseIds, date);

            if (!lectures.Any())
            {
                int dayOffset = (DateTime.UtcNow.Date - date).Days;
                string dayName = dayOffset switch
                {
                    -1 => "χθες",
                    0  => "σήμερα",
                    1  => "αύριο",
                    var o when o >= 2 && o <= 7 => $"την {date:dddd}",
                    _  => $"τις {date:d/M}"
                };

                await stepContext.Context.SendActivityAsync($"Δεν {(dayOffset >= 0 ? "έχεις" : "είχες")} μαθήματα για {dayName}! 😎");
            }
            else
            {
                var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 2))
                {
                    BackgroundImage = new AdaptiveBackgroundImage(AdaptiveCardsHelper.DarkBackgroundImageUrl)
                };
                card.Body.Add(new AdaptiveTextBlockHeaderLight($"{date:D}"));

                foreach (var lec in lectures)
                {
                    card.Body.Add(new AdaptiveTextBlockHeaderLight(lec.Course.NameWithSubcourse));
                    card.Body.Add(new AdaptiveRichFactSetLight("Ώρες ", $"{lec.StartDateTime:t} - {lec.EndDateTime:t}"));
                    if (lec.Classroom != null)
                        card.Body.Add(new AdaptiveRichFactSetLight("Αίθουσα ", lec.Classroom.Name, separator: true));
                    card.Body.Add(new AdaptiveRichFactSetLight("Κατάσταση ", lec.Status.ToGreekString(), separator: true));
                    card.Body.Add(new AdaptiveRichFactSetLight("Σχόλια ", string.IsNullOrEmpty(lec.Info) ? "-" : lec.Info, separator: true));
                }
                Attachment attachment = new(contentType: AdaptiveCard.ContentType, content: JObject.FromObject(card));
                await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(attachment));
            }

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt), new YesNoPromptOptions("Θα ήθελες να δεις το πρόγραμμα για άλλη ημέρα"));
        }

        #endregion
    }
}
