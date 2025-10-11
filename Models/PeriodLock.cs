// Models/PeriodLock.cs
namespace MNBEMART.Models
{
    public class PeriodLock
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public DateTime LockedAt { get; set; }
        public int LockedById { get; set; }

        public User LockedBy { get; set; } = null!;
    }
}
