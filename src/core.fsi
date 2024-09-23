
module Aestas.Core

type IMessageAdapter =
    
    abstract Mention: targetMemberId: uint32 -> bool
    
    abstract Parse: unit -> AestasMessage
    
    abstract ParseAsPlainText: unit -> AestasMessage
    
    abstract TryGetCommand: string seq -> (string * string) option
    
    abstract Collection: IMessageAdapterCollection with get
    
    abstract MessageId: uint64 with get
    
    abstract Preview: string with get
    
    abstract SenderId: uint32 with get

and [<Interface>] IMessageAdapterCollection =
    inherit System.Collections.Generic.IList<IMessageAdapter>
    inherit System.Collections.Generic.ICollection<IMessageAdapter>
    
    abstract Parse: unit -> AestasMessage array
    
    abstract Domain: AestasChatDomain with get
    
    abstract GetReverseIndex: int * int -> int with get

and [<Interface>] IProtocolAdapter =
    
    /// return: name * domainId * isPrivate
    abstract FetchDomains: unit -> struct (string * uint32 * bool) array
    
    abstract Init: unit -> unit
    
    abstract
      InitDomainView: bot: AestasBot -> domainId: uint32 -> AestasChatDomain
    
    abstract Run: unit -> Async<unit>

and [<AbstractClass; Class>] AestasChatDomain =
    
    new: unit -> AestasChatDomain
    
    override
      OnReceiveMessage: msg: IMessageAdapter -> Async<Result<unit,string>>
    
    abstract OnReceiveMessage: IMessageAdapter -> Async<Result<unit,string>>
    
    override Recall: messageId: uint64 -> Async<bool>
    
    abstract Recall: messageId: uint64 -> Async<bool>
    
    abstract
      Send: callback: (IMessageAdapter -> unit) ->
              contents: AestasContent list -> Async<Result<unit,string>>
    
    override
      SendFile: data: byte array ->
                  fileName: string -> Async<Result<unit,string>>
    
    abstract
      SendFile: data: byte array ->
                  fileName: string -> Async<Result<unit,string>>
    
    abstract Bot: AestasBot option with get, set
    
    abstract DomainId: uint32 with get
    
    abstract Members: AestasChatMember array with get
    
    abstract Messages: IMessageAdapterCollection with get
    
    abstract Name: string with get
    
    abstract Private: bool with get
    
    abstract Self: AestasChatMember with get
    
    abstract Virtual: AestasChatMember with get

and [<Class>] AestasChatDomains =
    System.Collections.Generic.Dictionary<uint32,AestasChatDomain>

and BotContentLoadStrategy =
    
    /// Load anything as plain text
    | StrategyLoadNone
    
    /// Only load media from messages that mentioned the bot or private messages
    | StrategyLoadOnlyMentionedOrPrivate
    
    /// Load all media from messages
    | StrategyLoadAll
    
    /// Load media from messages that satisfy the predicate
    | StrategyLoadByPredicate of (IMessageAdapter -> bool)

and BotFriendStrategy =
    | StrategyFriendNone
    | StrategyFriendAll
    | StrategyFriendWhitelist of
      System.Collections.Generic.Dictionary<uint32,Set<uint32>>
    | StrategyFriendBlacklist of
      System.Collections.Generic.Dictionary<uint32,Set<uint32>>

and BotMessageReplyStrategy =
    
    /// Won't reply to any message
    | StrategyReplyNone
    
    /// Only reply to messages that mentioned the bot or private messages
    | StrategyReplyOnlyMentionedOrPrivate
    
    /// Reply to all messages
    | StrategyReplyAll
    
    /// Reply to messages that satisfy the predicate
    | StrategyReplyByPredicate of (AestasMessage -> bool)

and BotMessageCacheStrategy =
    
    /// Clear cache after reply immediately
    | StrategyCacheNone
    
    /// Only cache messages that mentioned the bot or private messages
    | StrategyCacheOnlyMentionedOrPrivate
    
    /// Cache all messages
    | StrategyCacheAll

and BotContentParseStrategy =
    
    /// Parse all content to plain text
    | StrategyParseAllToPlainText
    
    /// Parse all content to AestasContent, ignore errors
    | StrategyParseAndIgnoreError
    
    /// Parse all content to AestasContent, alert errors like <Couldn't find function [{funcName}]>
    | StrategyParseAndAlertError
    
    /// Parse all content to AestasContent, restore content to original if error occurs
    | StrategyParseAndRestoreError

and BotContextStrategy =
    
    /// Trim context when exceed length
    | StrategyContextTrimWhenExceedLength of int
    
    /// Compress context when exceed length
    | StrategyContextCompressWhenExceedLength of int
    
    /// Reserve all context
    | StrategyContextReserveAll

/// interest is a function, ∈ [0, 100]
and BotInterestCurve =
    
    /// Bot will always interest in this domain
    | CurveInterestAlways
    
    /// Bot will lose interest in this domain after certain time
    | CurveInterestTruncateAfterTime of int
    
    /// Use your own function to determine interest
    | CurveInterestFunction of (int<Prim.sec> -> int)

/// Used in Model <-> Aestas.Core <-> Protocol
/// Provide extra content type. For example, market faces in QQ
and [<Interface>] IProtocolSpecifyContent =
    
    /// Convert this to something that only protocol can understand
    abstract Convert: AestasBot -> AestasChatDomain -> obj option
    
    abstract ToPlainText: unit -> string
    
    /// .NET type of this content
    abstract Type: System.Type with get

and [<Struct>] AestasChatMember =
    {
      uid: uint32
      name: string
    }

and AestasContent =
    | AestasBlank
    | AestasText of string
    
    /// byte array, mime type, width, height
    | AestasImage of byte array * string * int * int
    
    /// byte array, mime type, duration in seconds
    | AestasAudio of byte array * string * int
    | AestasVideo of byte array * string
    | AestasMention of AestasChatMember
    | AestasQuote of uint64
    | AestasFold of IMessageAdapterCollection
    | ProtocolSpecifyContent of IProtocolSpecifyContent

and [<Struct>] AestasMessage =
    {
      sender: AestasChatMember
      contents: AestasContent list
      mid: uint64
    }

and InputConverterFunc = IMessageAdapterCollection -> AestasContent -> string

/// domain * params * content
and 't ContentCtor =
    AestasBot -> AestasChatDomain -> (string * string) list -> string -> 't

and OverridePluginFunc = AestasContent ContentCtor

and ContentParser = Result<AestasContent,string> ContentCtor

and ProtocolSpecifyContentCtor =
    Result<IProtocolSpecifyContent,string> ContentCtor

and SystemInstructionBuilder = AestasBot -> System.Text.StringBuilder -> unit

and PrefixBuilder = AestasBot -> AestasMessage -> AestasMessage

/// Bot class used in Aestas Core
/// Create this directly
and [<Class>] AestasBot =
    
    new: unit -> AestasBot
    
    member AddCommandExecuter: key: string -> value: CommandExecuter -> unit
    
    member AddExtraData: key: string -> value: obj -> unit
    
    member BindDomain: domain: AestasChatDomain -> unit
    
    member CheckContextLength: domain: AestasChatDomain -> Async<unit>
    
    member ClearCachedContext: domain: AestasChatDomain -> unit
    
    member IsFriend: domain: AestasChatDomain -> uid: uint32 -> bool
    
    member
      ParseIMessageAdapter: message: IMessageAdapter ->
                              domain: AestasChatDomain ->
                              inQuote: bool -> AestasMessage
    
    member
      Recall: domain: AestasChatDomain ->
                messageId: uint64 -> Async<Result<unit,string>>
    
    member
      Reply: domain: AestasChatDomain ->
               message: IMessageAdapter ->
               Async<Result<AestasContent list,string> *
                     (IMessageAdapter -> unit)>
    
    member
      SelfTalk: domain: AestasChatDomain ->
                  content: AestasContent list option ->
                  Async<Result<unit,string>>
    
    member TryGetCommandExecuter: key: string -> CommandExecuter option
    
    member TryGetExtraData: key: string -> obj option
    
    member
      CommandExecuters: System.Collections.Generic.Dictionary<string,
                                                              CommandExecuter>
                          with get
    
    member ContentLoadStrategy: BotContentLoadStrategy with get, set
    
    member ContentParseStrategy: BotContentParseStrategy with get, set
    
    member
      ContentParsers: System.Collections.Generic.Dictionary<string,
                                                            struct
                                                              (ContentParser *
                                                               (AestasBot ->
                                                                  System.Text.StringBuilder ->
                                                                  unit))>
                        with get
    
    member ContextStrategy: BotContextStrategy with get, set
    
    member Domain: domainId: uint32 -> AestasChatDomain with get, set
    
    member
      Domains: System.Collections.Generic.IReadOnlyDictionary<uint32,
                                                              AestasChatDomain>
                 with get
    
    member ExtraData: System.Collections.Generic.Dictionary<string,obj> with get
    
    member FriendStrategy: BotFriendStrategy with get, set
    
    member
      MemberCommandPrivilege: System.Collections.Generic.Dictionary<uint32,
                                                                    CommandPrivilege>
                                with get
    
    member MessageCacheStrategy: BotMessageCacheStrategy with get, set
    
    member MessageReplyStrategy: BotMessageReplyStrategy with get, set
    
    member Model: ILanguageModelClient option with get, set
    
    member
      ModelInputConverter: (AestasChatDomain -> AestasContent -> string)
                             with get, set
    
    member
      ModelOutputParser: (AestasBot ->
                            AestasChatDomain -> string -> AestasContent list)
                           with get, set
    
    /// Bot name, Default is "AestasBot"
    member Name: string with get, set
    
    member PrefixBuilder: PrefixBuilder option with get, set
    
    member
      ProtocolContentCtorTips: System.Collections.Generic.Dictionary<string,
                                                                     struct
                                                                       (ProtocolSpecifyContentCtor *
                                                                        (AestasBot ->
                                                                           string))>
                                 with get
    
    member SystemInstruction: string with get, set
    
    member
      SystemInstructionBuilder: Prim.PipeLineChain<AestasBot *
                                                   System.Text.StringBuilder> option
                                  with get, set

and GenerationConfig =
    {
      
      /// factor to control the randomness of the generation
      temperature: float option
      
      /// maximum length of the generated text, in tokens
      maxLength: int option
      
      /// number of highest probability tokens to keep for top-k sampling
      topK: int option
      
      /// cumulative probability threshold for nucleus sampling, not supported by all models
      topP: float option
      
      /// frequency penalty to reduce the probability of repeating the same token, not supported by all models
      frequencyPenalty: float option
      
      /// when arives string in this list, the model will stop generating
      stop: string list option
    }

and CacheMessageCallback = AestasMessage -> unit

and [<Interface>] ILanguageModelClient =
    
    /// bot * domain * contents -> unit, with no sender, dont send the message
    abstract
      CacheContents: bot: AestasBot ->
                       domain: AestasChatDomain ->
                       contents: AestasContent list -> unit
    
    /// bot * domain * message -> unit, with a certain sender in AestasMessage, dont send the message
    abstract
      CacheMessage: bot: AestasBot ->
                      domain: AestasChatDomain -> message: AestasMessage -> unit
    
    abstract ClearCache: AestasChatDomain -> unit
    
    /// bot -> domain -> message -> response * callback
    abstract
      GetReply: bot: AestasBot ->
                  domain: AestasChatDomain ->
                  Async<Result<AestasContent list,string> * CacheMessageCallback>
    
    abstract RemoveCache: domain: AestasChatDomain -> messageId: uint64 -> unit

and [<Struct>] CommandAccessibleDomain =
    | None = 0
    | Private = 1
    | Group = 2
    | All = 3

and [<Struct>] CommandPrivilege =
    | BelowNormal = 0
    | Normal = 1
    | High = 2
    | AboveHigh = 3

and CommandEnvironment =
    {
      bot: AestasBot
      domain: AestasChatDomain
      log: (string -> unit)
      privilege: CommandPrivilege
    }

and [<AbstractClass; Class>] CommandExecuter =
    
    new: unit -> CommandExecuter
    
    abstract Execute: CommandEnvironment -> string -> unit

and [<AbstractClass; Class>] CommandExecuter<'t> =
    inherit CommandExecuter
    
    new: commands: 't seq -> CommandExecuter<'t>
    
    override AddCommand: command: 't -> unit
    
    abstract AddCommand: 't -> unit
    
    abstract Commands: 't Prim.arrList with get
    
    override Commands: 't Prim.arrList with get

and TextToImageArgument =
    {
      prompt: string
      negative: string
      resolution: int * int
      seed: int option
    }

and [<Class>] UnitMessageAdapter =
    interface IMessageAdapter
    
    new: message: AestasMessage * collection: UnitMessageAdapterCollection ->
           UnitMessageAdapter

and [<Class>] UnitMessageAdapterCollection =
    inherit IMessageAdapter Prim.arrList
    interface IMessageAdapterCollection
    
    new: domain: VirtualDomain -> UnitMessageAdapterCollection

and [<Class>] VirtualDomain =
    inherit AestasChatDomain
    
    new: send: (uint64 -> AestasContent list -> unit) * recall: (uint64 -> unit) *
         bot: AestasChatMember * user: AestasChatMember * domainId: uint32 *
         domainName: string * isPrivate: bool -> VirtualDomain
    
    member
      Input: contents: AestasContent list -> Async<Result<unit,string> * uint64>
    
    override Recall: messageId: uint64 -> Async<bool>
    
    override
      Send: callback: (IMessageAdapter -> unit) ->
              contents: AestasContent list -> Async<Result<unit,string>>
    
    override Bot: AestasBot option with get, set
    
    override DomainId: uint32 with get
    
    override Members: AestasChatMember array with get
    
    override Messages: IMessageAdapterCollection with get
    
    override Name: string with get
    
    override Private: bool with get
    
    override Self: AestasChatMember with get
    
    override Virtual: AestasChatMember with get

and [<Class>] UnitClient =
    interface ILanguageModelClient
    
    new: unit -> UnitClient

val defaultGenerationConfig: GenerationConfig

val noneGenerationConfig: GenerationConfig

module Logger =
    
    [<Struct>]
    type LogLevel =
        | Trace = 0
        | Debug = 1
        | Info = 2
        | Warn = 3
        | Error = 4
        | Fatal = 5
    
    and [<Struct>] LogEntry =
        {
          time: System.DateTime
          level: LogLevel
          message: string
        }
        
        member Print: unit -> string
    
    val inline levelToColor: lv: LogLevel -> System.ConsoleColor
    
    val internal logs:
      System.Collections.Generic.Dictionary<obj,LogEntry Prim.arrList>
    
    val onLogUpdate: (LogEntry -> unit) Prim.arrList
    
    val getLoggerOwners: unit -> obj array
    
    val getLogs: o: 'a -> System.Collections.Generic.IReadOnlyList<LogEntry>
    
    val inline internal _log: key: obj -> lv: LogLevel -> s: string -> unit
    
    val inline internal __log: lv: LogLevel -> key: obj -> s: string -> unit
    
    val log: Prim.IndexerBox<obj,(LogLevel -> string -> unit)>
    
    val logTrace: Prim.IndexerBox<obj,(string -> unit)>
    
    val logDebug: Prim.IndexerBox<obj,(string -> unit)>
    
    val logInfo: Prim.IndexerBox<obj,(string -> unit)>
    
    val logWarn: Prim.IndexerBox<obj,(string -> unit)>
    
    val logError: Prim.IndexerBox<obj,(string -> unit)>
    
    val logFatal: Prim.IndexerBox<obj,(string -> unit)>
    
    val inline internal _logf:
      key: obj -> lv: LogLevel -> fmt: Printf.StringFormat<'t,unit> -> 't
    
    val inline internal __logf:
      lv: LogLevel -> key: obj -> fmt: Printf.StringFormat<'t,unit> -> 't
    
    val logf:
      Prim.IndexerBox<obj,(LogLevel -> Printf.StringFormat<'t,unit> -> 't)>
    
    val logTracef: Prim.IndexerBox<obj,(Printf.StringFormat<'t,unit> -> 't)>
    
    val logDebugf: Prim.IndexerBox<obj,(Printf.StringFormat<'t,unit> -> 't)>
    
    val logInfof: Prim.IndexerBox<obj,(Printf.StringFormat<'t,unit> -> 't)>
    
    val logWarnf: Prim.IndexerBox<obj,(Printf.StringFormat<'t,unit> -> 't)>
    
    val logErrorf: Prim.IndexerBox<obj,(Printf.StringFormat<'t,unit> -> 't)>
    
    val logFatalf: Prim.IndexerBox<obj,(Printf.StringFormat<'t,unit> -> 't)>

module ConsoleBot =
    
    type ConsoleDomain =
        inherit AestasChatDomain
        
        new: botName: string * user: AestasChatMember -> ConsoleDomain
        
        override Recall: messageId: uint64 -> Async<bool>
        
        override
          Send: callback: (IMessageAdapter -> unit) ->
                  msgs: AestasContent list -> Async<Result<unit,string>>
        
        override Bot: AestasBot option with get, set
        
        member CachedContext: string Prim.arrList with get
        
        override DomainId: uint32 with get
        
        member InnerMid: uint64 with get, set
        
        override Members: AestasChatMember array with get
        
        override Messages: IMessageAdapterCollection with get
        
        override Name: string with get
        
        override Private: bool with get
        
        override Self: AestasChatMember with get
        
        override Virtual: AestasChatMember with get
    
    and [<Class>] ConsoleMessageCollection =
        inherit IMessageAdapter Prim.arrList
        interface IMessageAdapterCollection
        
        new: consoleDomain: ConsoleDomain -> ConsoleMessageCollection
        
        member Domain: ConsoleDomain with get
        
        member MsgList: IMessageAdapter Prim.arrList with get
    
    and [<Class>] ConsoleMessage =
        interface IMessageAdapter
        
        new: collection: ConsoleMessageCollection * sender: AestasChatMember *
             msg: string -> ConsoleMessage
        
        member Sender: AestasChatMember with get
    
    and [<Class>] ConsoleChat =
        interface IProtocolAdapter
        
        new: unit -> ConsoleChat
        
        /// only for cli
        member BotContext: b: AestasBot -> string Prim.arrList
        
        member FetchDomains: unit -> struct (string * uint32 * bool) array
        
        member Init: unit -> unit
        
        member
          InitDomainView: bot: AestasBot * domainId: uint32 -> ConsoleDomain
        
        member Run: unit -> Async<unit>
        
        /// only for cli
        member Send: bot: AestasBot * msg: string -> unit
        
        /// only for cli
        member BindedBots: AestasBot Prim.arrList with get
        
        member ConsoleHook: (unit -> unit) with get, set
    
    val singleton: ConsoleChat

module Builtin =
    
    type SpacedTextCommand =
        {
          name: string
          description: string
          accessibleDomain: CommandAccessibleDomain
          privilege: CommandPrivilege
          execute:
            (SpacedTextCommandExecuter ->
               CommandEnvironment -> string array -> unit)
        }
    
    and [<Class>] SpacedTextCommandExecuter =
        inherit CommandExecuter<SpacedTextCommand>
        
        new: commands': SpacedTextCommand list -> SpacedTextCommandExecuter
        
        override AddCommand: cmd: SpacedTextCommand -> unit
        
        override Execute: env: CommandEnvironment -> cmd: string -> unit
        
        override Commands: SpacedTextCommand Prim.arrList with get
    
    val inline toString: o: obj -> string
    
    val modelInputConverters:
      domain: AestasChatDomain -> _arg18: AestasContent -> string
    
    val overridePrimCtor:
      System.Collections.Generic.Dictionary<string,
                                            (AestasChatDomain *
                                             (string * string) list * string ->
                                               AestasContent)>
    
    val overridePrimTip: (string * string) list
    
    val modelOutputParser:
      bot: AestasBot ->
        domain: AestasChatDomain -> botOut: string -> AestasContent list
    
    val buildSystemInstruction:
      bot: AestasBot * sb: System.Text.StringBuilder ->
        AestasBot * System.Text.StringBuilder
    
    val buildPrefix: bot: AestasBot -> msg: AestasMessage -> AestasMessage
    
    val versionCommand: unit -> SpacedTextCommand
    
    val clearCommand: unit -> SpacedTextCommand
    
    val helpCommand: unit -> SpacedTextCommand
    
    val echoCommand: unit -> SpacedTextCommand
    
    val lsfrwlCommand: unit -> SpacedTextCommand
    
    val lsfrblCommand: unit -> SpacedTextCommand
    
    val ufrwlCommand: unit -> SpacedTextCommand
    
    val ufrblCommand: unit -> SpacedTextCommand
    
    val commands: unit -> SpacedTextCommand list

module AestasBot =
    
    val inline tryGetModel: bot: AestasBot -> ILanguageModelClient option
    
    val inline bindModel: bot: AestasBot -> model: ILanguageModelClient -> unit
    
    val inline bindDomain: bot: AestasBot -> domain: AestasChatDomain -> unit
    
    val inline addExtraData: bot: AestasBot -> key: string -> value: obj -> unit
    
    val inline addCommandExecuter:
      bot: AestasBot -> key: string -> executer: CommandExecuter -> unit
    
    val inline addContentParser:
      bot: AestasBot ->
        ctor: ContentParser * name: string *
        tip: (AestasBot -> System.Text.StringBuilder -> unit) -> unit
    
    val inline addProtocolContentCtorTip:
      bot: AestasBot ->
        ctor: ProtocolSpecifyContentCtor * name: string *
        tip: (AestasBot -> string) -> unit
    
    val inline updateExtraData:
      bot: AestasBot -> key: string -> value: obj -> unit
    
    val inline updateContentParser:
      bot: AestasBot ->
        ctor: ContentParser * name: string *
        tip: (AestasBot -> System.Text.StringBuilder -> unit) -> unit
    
    val inline addCommandExecuters:
      bot: AestasBot -> cmds: (string * CommandExecuter) list -> unit
    
    val inline addContentParsers:
      bot: AestasBot ->
        contentParsers: (ContentParser * string *
                         (AestasBot -> System.Text.StringBuilder -> unit)) list ->
        unit
    
    val inline addProtocolContentCtorTips:
      bot: AestasBot ->
        ctorTips: (ProtocolSpecifyContentCtor * string * (AestasBot -> string)) list ->
        unit
    
    val inline addSystemInstruction:
      bot: AestasBot -> systemInstruction: string -> unit
    
    val inline addSystemInstructionBuilder:
      bot: AestasBot ->
        systemInstructionBuilder: (AestasBot * System.Text.StringBuilder ->
                                     AestasBot * System.Text.StringBuilder) ->
        unit
    
    val inline updateSystemInstruction:
      bot: AestasBot -> systemInstruction: string -> unit
    
    val inline updateSystemInstructionBuilder:
      bot: AestasBot ->
        systemInstructionBuilder: Prim.PipeLineChain<AestasBot *
                                                     System.Text.StringBuilder> ->
        unit
    
    val builtinCommandsExecuter: unit -> Builtin.SpacedTextCommandExecuter
    
    val makeExecuterWithBuiltinCommands:
      list: Builtin.SpacedTextCommand list -> Builtin.SpacedTextCommandExecuter
    
    val inline createBot:
      botParam: {| contentLoadStrategy: BotContentLoadStrategy option;
                   contentParseStrategy: BotContentParseStrategy option;
                   contextStrategy: BotContextStrategy option;
                   friendStrategy: BotFriendStrategy option;
                   inputPrefixBuilder: PrefixBuilder option;
                   messageCacheStrategy: BotMessageCacheStrategy option;
                   messageReplyStrategy: BotMessageReplyStrategy option;
                   model: ILanguageModelClient; name: string;
                   systemInstruction: string option;
                   systemInstructionBuilder: Prim.PipeLineChain<AestasBot *
                                                                System.Text.StringBuilder> option;
                   userCommandPrivilege: (uint32 * CommandPrivilege) list option |} ->
        AestasBot
    
    val inline createBotShort: name: string -> AestasBot

module CommandExecuter =
    
    val inline tryAddCommand:
      executer: CommandExecuter -> cmd: 't -> Result<unit,string>
    
    val inline getCommands: executer: CommandExecuter<'t> -> 't Prim.arrList

