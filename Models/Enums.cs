namespace MNBEMART.Models
{
    public enum DocumentStatus
    {
        Moi = 0,            // vừa tạo
        DaXacNhan = 1,      // đã duyệt nội bộ, hàng sắp về
        DaNhapHang = 2,     // đã về kho, ĐÃ cộng tồn
        DaXuatHang = 3,     // đã xuất kho, ĐÃ trừ tồn
        HoanThanh = 4,      // hoàn thành, không còn gì phải làm nữa
        DaHuy = 9           // hủy; nếu đã cộng tồn thì phải TRỪ lại
    }

    public enum CostingMethod
    {
        FIFO = 0,              // First In First Out
        WeightedAverage = 1    // Bình quân gia quyền
    }
}
