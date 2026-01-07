namespace MNBEMART.Models
{
    public class MaterialSpecification
    {
        public int Id { get; set; }
        public string Name { get; set; } // gram, kg, ML, thanh, ...
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}




















































