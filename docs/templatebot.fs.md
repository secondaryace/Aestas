```fsharp
namespace Aestas.Bots
open System.Collections.Generic
open System.Linq
open Aestas.Prim
open Aestas.Core
open Aestas.AutoInit
open Aestas.Adapters
open Aestas.Llms.Gemini
open Aestas.Plugins.MsTts

type TemplateBot =
    // implement IAutoInit to automatically initialize the bot
    interface IAutoInit<AestasBot, unit> with
        static member Init _ =
            // create a new bot instance
            let bot = AestasBot()
            // create a new language model instance
            let model = GeminiLlm({
                api_key = Some "{Your API key here}"
                gcloudpath = None
                safetySettings = [|
                    {category = "HARM_CATEGORY_HARASSMENT"; threshold = "BLOCK_SOME"}
                    {category = "HARM_CATEGORY_HATE_SPEECH"; threshold = "BLOCK_SOME"}
                    {category = "HARM_CATEGORY_SEXUALLY_EXPLICIT"; threshold = "BLOCK_SOME"}
                    {category = "HARM_CATEGORY_DANGEROUS_CONTENT"; threshold = "BLOCK_SOME"}
                |]
                generation_configs = ["gemini-1.5-flash-latest", {
                    temperature = 1.02
                    max_length = 4096
                    top_k = 64
                    top_p = 1.
                }] |> dict |> Some
                }, true)
            bot.Name <- "TemplateBot"
            bot.ReplyStrategy <- StrategyReplyOnlyMentionedOrPrivate
            bot.SystemInstruction <- """{Your system instruction here}"""
            bot.Model <- Some model
            // bind the bot to the domain
            protocols["lagrange"].InitDomainView(bot, 10001u) |> bot.BindDomain
            ConsoleBot.singleton.InitDomainView(bot, 0u) |> bot.BindDomain
            bot.PrefixBuilder <- Some Builtin.buildPrefix
            bot.Commands.Append commands
            mappingContentCtors |> Dict.addRange bot.MappingContentConstructors [|
                "voice"
            |]
            bot.SystemInstructionBuilder <- Some Builtin.buildSystemInstruction
            bot.AddExtraData "mstts" {
                subscriptionKey = "{Your subscription key here}"
                subscriptionRegion = "{Your subscription region here}"
                voiceName = "{Your voice name here}"
                outputFormat = "ogg-24khz-16bit-mono-opus"
                }
            bot
```