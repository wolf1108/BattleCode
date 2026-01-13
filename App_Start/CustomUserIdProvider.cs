using Microsoft.AspNet.SignalR;

namespace BattleCode.Providers // 用你的命名空間
{
    public class CustomUserIdProvider : IUserIdProvider
    {
        public string GetUserId(IRequest request)
        {
            // 這裡會回傳登入時設下的 UserId 字串
            return request.User?.Identity?.Name;
        }
    }
}