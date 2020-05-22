﻿using System;
using System.Collections.Generic;

namespace Phoenix.DataHandle.Main.Models
{
    public partial class Schedule
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        public int ClassroomId { get; set; }
        public int DayOfWeek { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Info { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public virtual Classroom Classroom { get; set; }
        public virtual Course Course { get; set; }
    }
}
