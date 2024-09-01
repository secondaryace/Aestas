namespace Aestas.CSharp
{
    public abstract class CSharpChatDomain: AestasChatDomain
    {
        public abstract Task SendAsync(Action<IMessageAdapter> callback, List<AestasContent> contents);
        public override FSharpAsync<FSharpResult<Unit, string>> Send(FSharpFunc<IMessageAdapter, Unit> value0, FSharpList<AestasContent> value1)
        {
            async Task<FSharpResult<Unit, string>> wrapper() 
            {
                await SendAsync(message => value0.Invoke(message), value1.ToList());
                return FSharpResult<Unit, string>.NewOk(null);
            }
            return FSharpAsync.AwaitTask(wrapper());
        }
    }
}
