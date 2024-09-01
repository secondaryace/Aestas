namespace Aestas.CSharp
{
    public interface ITaskStyleLlmClient: ILanguageModelClient
    {
        public abstract Task<(List<AestasContent>, Action<AestasMessage>)> GetReplyAsync(AestasBot bot, AestasChatDomain domain);
        FSharpAsync<Tuple<FSharpResult<FSharpList<AestasContent>, string>, FSharpFunc<AestasMessage, Unit>>> ILanguageModelClient.GetReply(AestasBot bot, AestasChatDomain domain)
        {
            async Task<Tuple<FSharpResult<FSharpList<AestasContent>, string>, FSharpFunc<AestasMessage, Unit>>> wrapper()
            {
                var (contents, reply) = await GetReplyAsync(bot, domain);
                return Tuple.Create(FSharpResult<FSharpList<AestasContent>, string>.NewOk(ListModule.OfSeq(contents)), FuncConvert.FromAction(reply));
            }
            return FSharpAsync.AwaitTask(wrapper());
        }
    }
}
