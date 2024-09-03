using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aestas.CSharp
{
    public static class BotExtension
    {
        public static async Task<(bool, List<AestasContent>, Action<IMessageAdapter>)> ReplyAsync(this AestasBot bot, AestasChatDomain domain, IMessageAdapter message)
        {
            var (result, callback) = await FSharpAsync.StartAsTask(bot.Reply(domain, message), null, null);

            return (result.IsError, result.ResultValue.ToList(), message => callback.Invoke(message));
        }
        public static async Task<bool> SelfTalkAsync(this AestasBot bot, AestasChatDomain domain, List<AestasContent>? contents)
        {
            var result = await FSharpAsync.StartAsTask(
                bot.SelfTalk(domain, contents is null 
                ? FSharpOption<FSharpList<AestasContent>>.None 
                : FSharpOption<FSharpList<AestasContent>>.Some(ListModule.OfSeq(contents)))
                , null, null);
            return result.IsError;
        }
        public static async Task<bool> RecallAsync(this AestasBot bot, AestasChatDomain domain, ulong messageId)
        {
            var result = await FSharpAsync.StartAsTask(bot.Recall(domain, messageId), null, null);
            return result.IsError;
        }
        public static Func<AestasBot, AestasMessage, AestasMessage>? TryGetPrefixBuilder(this AestasBot bot)
        {
            if (FSharpOption<FSharpFunc<AestasBot, FSharpFunc<AestasMessage, AestasMessage>>>.get_IsSome(bot.PrefixBuilder))
            {
                return (bot, message) => bot.PrefixBuilder.Value.Invoke(bot).Invoke(message);
            }
            return null;
        }
        public static void SetPrefixBuilder(this AestasBot bot, Func<AestasBot, AestasMessage, AestasMessage>? prefixBuilder)
        {
            if (prefixBuilder is null)
            {
                bot.PrefixBuilder = FSharpOption<FSharpFunc<AestasBot, FSharpFunc<AestasMessage, AestasMessage>>>.None;
            }
            else 
            {
                bot.PrefixBuilder = FSharpHelper.MakeSome(FuncConvert.FromFunc(prefixBuilder));
            }
        }
        public static ILanguageModelClient? TryGetModel(this AestasBot bot)
        {
            if (FSharpOption<ILanguageModelClient>.get_IsSome(bot.Model))
            {
                return bot.Model.Value;
            }
            return null;
        }
        public static void SetModel(this AestasBot bot, ILanguageModelClient? model)
        {
            if (model is null)
            {
                bot.Model = FSharpOption<ILanguageModelClient>.None;
            }
            else
            {
                bot.Model = FSharpHelper.MakeSome(model);
            }
        }
        public static PipeLineChain<Tuple<AestasBot, StringBuilder>>? TryGetSystemInstructionBuilder(this AestasBot bot)
        {
            if (FSharpOption<PipeLineChain<Tuple<AestasBot, StringBuilder>>>.get_IsSome(bot.SystemInstructionBuilder))
            {
                return bot.SystemInstructionBuilder.Value;
            }
            return null;
        }
        public static void SetSystemInstructionBuilder(this AestasBot bot, PipeLineChain<Tuple<AestasBot, StringBuilder>>? chain)
        {
            if (chain is null)
            {
                bot.SystemInstructionBuilder = FSharpOption<PipeLineChain<Tuple<AestasBot, StringBuilder>>>.None;
            }
            else
            {
                bot.SystemInstructionBuilder = FSharpHelper.MakeSome(chain);
            }
        }
    }
}
