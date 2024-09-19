namespace Aestas.Commands
open Aestas.Core
open Aestas.Commands.AestasScript
open Aestas.Commands.Compiler.Runtime

open System.Net.Http

module GuessWordCommand =
    let execute executer env ctx args =
        let randomWord =
            use web = new HttpClient()
            web.GetStringAsync("https://random-word-api.herokuapp.com/word?lang=en").Result.Trim('[', ']', '"')
        let prompt = $"""接下来我们一起来玩一个猜单词小游戏吧！
你是唯一知道这个单词的人，而且你不可以直接告诉别人这个单词，除非他们猜对或者极为接近正确答案，要么是认输。
规则是，举个例子，你有一个单词是"read"，有4个字母，你可以告诉别人这个词有4个字母，类似_ _ _ _。
当他们给你一个单词，比如"heat"，你告诉他们其中e和a是匹配的，这个单词类似_ e a _。
现在，给你一个单词{randomWord}，有{randomWord.Length}个字母。这个单词是唯一的正确答案。在游戏结束时，公开谜底。
开始游戏吧。如果有人希望你减轻难度，给一些提示也是可以的。"""
        [AestasText prompt] |> Some |> env.bot.SelfTalk env.domain |> Async.Ignore |> Async.Start; ctx, Tuple []
    let make() = {
        name = "guessword"
        description = "Play a word guessing game"
        accessibleDomain = CommandAccessibleDomain.All
        privilege = CommandPrivilege.Normal
        execute = execute
    }