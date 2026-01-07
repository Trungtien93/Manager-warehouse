using System.Threading;
using System.Threading.Tasks;

namespace MNBEMART.Services
{
    public interface IChatService
    {
        /// <summary>
        /// Gửi câu hỏi lên AI và trả về câu trả lời dạng text thuần.
        /// </summary>
        Task<string> AskAsync(string message, CancellationToken cancellationToken = default);
    }
}
