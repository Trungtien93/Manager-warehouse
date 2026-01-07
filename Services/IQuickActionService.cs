namespace MNBEMART.Services
{
    public interface IQuickActionService
    {
        Task<bool> QuickApprovePurchaseRequestAsync(int purchaseRequestId, int userId);
        Task<bool> QuickRejectPurchaseRequestAsync(int purchaseRequestId, int userId, string? reason = null);
        Task<bool> QuickConfirmReceiptAsync(int receiptId, int userId);
        Task<bool> QuickCancelReceiptAsync(int receiptId, int userId);
        Task<bool> QuickApproveUserAsync(int userId, int approvedBy);
        Task<bool> QuickRejectUserAsync(int userId, int rejectedBy, string? reason = null);
    }
}



