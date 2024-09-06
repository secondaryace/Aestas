using Aestas;
using static Aestas.Prim;
using static Aestas.Core;
using Aestas.CSharp;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Microsoft.FSharp.Collections;
using Console = System.Console;

namespace Aestas.WebUI
{
    public class WebUI
    {
        public class WebUIAdapter: IProtocolAdapter, AutoInit.IAutoInit<IProtocolAdapter, Unit>
        {
            public void Init()
            {

            }
            public FSharpAsync<Unit> Run()
            {
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
                async Task f()
                {
                }
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
                return FSharpAsync.AwaitTask(f());
            }
            public (string, uint, bool)[] FetchDomains()
            {
                return [];
            }
            public AestasChatDomain InitDomainView(AestasBot bot, uint domainId)
            {
                bool isPrivate = domainId % 2 == 0;
                if (!Singleton.sendCallback.ContainsKey(bot))
                    Singleton.sendCallback.Add(bot, (_, _) => { });
                void send(ulong mid, FSharpList<AestasContent> x) =>
                    Singleton.sendCallback[bot](mid, ArrayModule.OfList(x));
                void recall(ulong x) { }
                if (!Singleton.domains.ContainsKey(bot)) 
                    Singleton.domains.Add(bot, new VirtualDomain(
                        FuncConvert.FromAction((Action<ulong, FSharpList<AestasContent>>)send),
                        FuncConvert.FromAction((Action<ulong>)recall),
                        new AestasChatMember(1u, bot.Name),
                        new AestasChatMember(0u, "WebUI Input"),
                        domainId,
                        "WebUI",
                        isPrivate
                        ));
                return Singleton.domains[bot];
            }
            public static IProtocolAdapter Init(Unit _)
            {
                return new WebUIAdapter();
            }
        }
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        public static WebUI Singleton { get; private set; }
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑添加 "required" 修饰符或声明为可为 null。
        public Dictionary<AestasBot, VirtualDomain> domains;
        public Dictionary<AestasBot, Action<ulong, AestasContent[]>> sendCallback;
        public WebUI()
        {
            Singleton = this;
            domains = [];
            sendCallback = [];
        }
        public void Initialize()
        {
            Console.WriteLine($"Working in path {Environment.CurrentDirectory}");
            Console.WriteLine("AutoInit.initAll()");
            Logger.logTrace[0].Invoke("test0");
            Logger.logDebug[0].Invoke("test1");
            Logger.logInfo[0].Invoke("test2");
            Logger.logWarn[0].Invoke("test3");
            Logger.logError[0].Invoke("test4");
            Logger.logFatal[0].Invoke("test5");
            AutoInit.initAll();
        }
        public async Task<ulong> SendMessage(VirtualDomain domain, AestasContent[] contents)
        {
            var (result, mid) = await FSharpAsync.StartAsTask(domain.Input(ListModule.OfArray(contents)), null, null);
            return mid;
        }
    }
}
