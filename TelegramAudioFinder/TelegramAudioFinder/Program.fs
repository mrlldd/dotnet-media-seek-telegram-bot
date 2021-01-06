module TelegramAudioFinder.Program

open System
open System.Threading
open System.Threading.Tasks
open Google.Apis.Services
open Google.Apis.Util
open Google.Apis.YouTube.v3
open Google.Apis.YouTube.v3.Data
open Newtonsoft.Json
open Suave
open CORS
open Filters
open Successful
open Operators
open Writers
open Telegram.Bot.Types
open Telegram.Bot.Types.Enums
open Telegram.Bot.Types.InlineQueryResults
open TelegramAudioFinder
open Utils
open TelegramBot
open Restful

open System.Linq

let cts = new CancellationTokenSource()


let isCtrlC (consoleKeyInfo: ConsoleKeyInfo) =
    consoleKeyInfo.Key = ConsoleKey.C
    && consoleKeyInfo.Modifiers = ConsoleModifiers.Control

let rec untilCloseCombination closeCombination =
    if not (Console.ReadKey true |> closeCombination)
    then untilCloseCombination closeCombination

let defaultHandler (r: HttpRequest) =
    OK(String.Format("well met {0}", r.path))

let telegramEndpoint = "/api/telegram"

let service =
    let init = BaseClientService.Initializer()
    init.ApiKey <- applicationConfig.Keys.Youtube
    init.ApplicationName <- "sample"
    new YouTubeService(init)

let curriedSender queryId items =
    bot.AnswerInlineQueryAsync(queryId, items) |> Async.AwaitTask

let mapper (index, item: SearchResult) =
    let snippet = item.Snippet
    let url = sprintf "https://youtube.com/watch?v=%s" item.Id.VideoId
    let res = InlineQueryResultArticle(index.ToString(), snippet.Title, InputTextMessageContent(url))
    res.Description <- snippet.Description
    res.ThumbUrl <- snippet.Thumbnails.Standard.Url
    res :> InlineQueryResultBase


let handler (update: Update) =
    fun (ctx: HttpContext) ->
        async {
            let sender = curriedSender update.InlineQuery.Id
            let list =
                service.Search.List(Repeatable(["snippet"]))
            list.Q <- update.InlineQuery.Query
            list.MaxResults <- int64(5)
            let! resp = list.ExecuteAsync(cts.Token) |> Async.AwaitTask
            printfn "%s" (json (resp, Formatting.Indented))
            do! resp.Items |> Seq.indexed |> Seq.map mapper |> sender  
            return! OK String.Empty ctx
        }

let startupAsync (cts: CancellationTokenSource) =
    let conf =
        { defaultConfig with
              cancellationToken = cts.Token }

    printfn "Starting up..."
    printfn "%s" (json (defaultCORSConfig, Formatting.Indented))
    printfn "%s" (json (applicationConfig, Formatting.Indented))

    let telegramConfig = applicationConfig.Telegram

    let webPath =
        choose [ path "/" >=> (OK "Bruh, here we go again")
                 path telegramEndpoint >=> POST >=> restful handler
                 request defaultHandler ]

    let res = startWebServerAsync conf webPath

    let url =
        sprintf "%s%s" telegramConfig.WebhookUrl telegramEndpoint

    printfn "Setting up webhook: %s" url

    bot.SetWebhookAsync
        (url, maxConnections = telegramConfig.MaxConnections, allowedUpdates = [ UpdateType.InlineQuery ])
    |> Async.AwaitTask
    |> ignore

    printfn "Set webhook: %s" url
    res


[<EntryPoint>]
let main _ =

    let _, server = startupAsync cts

    Async.Start(server, cts.Token)
    untilCloseCombination isCtrlC
    printfn "Shutting down..."
    cts.Cancel(false)
    0
