
namespace Aestas.CSharp
{
    public abstract class CSharpChatDomain: AestasChatDomain
    {
        public abstract Task SendAsync(Action<IMessageAdapter> callback, List<AestasContent> contents);
        public override FSharpAsync<FSharpResult<Unit, string>> Send(FSharpFunc<IMessageAdapter, Unit> callback, FSharpList<AestasContent> contents)
        {
            async Task<FSharpResult<Unit, string>> wrapper() 
            {
                await SendAsync(message => callback.Invoke(message), contents.ToList());
                return FSharpResult<Unit, string>.NewOk(FSharpHelper.UnitValue);
            }
            return FSharpAsync.AwaitTask(wrapper());
        }
    }
}
