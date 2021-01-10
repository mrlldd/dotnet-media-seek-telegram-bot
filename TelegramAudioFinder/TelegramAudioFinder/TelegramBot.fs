module TelegramAudioFinder.TelegramBot

open System
open System.Net
open Microsoft.FSharp.Collections
open Newtonsoft.Json
open Suave
open Successful
open Telegram.Bot
open Telegram.Bot.Types
open Telegram.Bot.Types.InlineQueryResults
open TelegramAudioFinder.Search.SoundCloud
open TelegramAudioFinder.Search.YouTube
open TelegramAudioFinder.Utils
open TelegramAudioFinder.Services.Utils

let bot =
    TelegramBotClient(applicationConfig.Telegram.Token)

let private curryingAnswerSender queryId offset items =
    printfn "%i" (offset)

    bot.AnswerInlineQueryAsync
        (queryId,
         items,
         nextOffset = (offset |> string),
         cacheTime = defaultCacheTimeout.Seconds,
         cancellationToken = cts.Token)
    |> Async.AwaitTask

[<Literal>]
let private thumbSideSize = 100

let searchResultsMapper (offset: int) (index, item: UnifiedSearchResult) =
    let res =
        InlineQueryResultArticle
            ((index + offset) |> string, item.Title |> WebUtility.HtmlDecode, InputTextMessageContent(item.Url))

    match item.ThumbUrl with
    | Some s -> res.ThumbUrl <- s
    | _ -> ()

    match item.Description with
    | Some s -> res.Description <- s
    | _ -> ()

    res.ThumbHeight <- thumbSideSize
    res.ThumbWidth <- thumbSideSize
    res :> InlineQueryResultBase

let private sendErrorToDeveloperAsync (inlineQuery: InlineQuery, error: exn) =
    bot.SendTextMessageAsync
        (applicationConfig.Telegram.DeveloperChatId
         |> ChatId,
         sprintf "Something went wrong.\nQuery: %s\nError: %s" (json (inlineQuery, Formatting.Indented))
             (json (error, Formatting.Indented)))
    |> Async.AwaitTask
    |> Async.Ignore


[<Literal>]
let private minimumKeywordLength = 3

let private helpDescription =
    "Perform media search in services such as YouTube and SoundCloud\n"
    + "Example: astrothunder\n"
    + "/?h or /?help for help\n"
    + "/?y or /?youtube for search with YouTube\n"
    + "/?s or /?soundcloud for search with SoundCloud\n"
    + "/?a or /?all for search with all available services\n"
    + "Example: /?a astrothunder\n"
    + "Query without command arguments will perform search in all available services by default.\n"
    + $"Minimum keyword length: {minimumKeywordLength}"

let private helpAsync (inlineQuery: InlineQuery, token) =
    let article =
        InlineQueryResultArticle("0", "Help", InputTextMessageContent(helpDescription))

    article.Description <- helpDescription

    bot.AnswerInlineQueryAsync(inlineQuery.Id, [ article ], 0, cancellationToken = token)
    |> Async.AwaitTask

type private Command =
    | Help
    | Search of Service
    | Wrong of string

let modifyQueryString (inlineQuery: InlineQuery, modified) =
    inlineQuery.Query <- modified
    inlineQuery

let private readSearchQuery command =
    match command with
    | "/?y"
    | "/?youtube" -> Search YouTube
    | "/?s"
    | "/?soundcloud" -> Search SoundCloud
    | "/?a"
    | "/?all" -> Search All
    | "/?h"
    | "/?help" -> Help
    | other -> Wrong other

let private readQuery (inlineQuery: InlineQuery) =
    let queryString = inlineQuery.Query
    let index = queryString.IndexOf(' ')

    if index = -1 then
        match queryString with
        | "/?h"
        | "/?help" -> Help, inlineQuery
        | command when command.StartsWith("/?") -> Wrong command, inlineQuery
        | _ -> Search All, inlineQuery
    else
        let (command, keywords) =
            queryString.Substring(0, index), queryString.Substring(index)

        match command with
        | command when command.StartsWith("/?") ->
            match readSearchQuery command with
            | Wrong _
            | Help -> Wrong command, inlineQuery
            | search -> search, modifyQueryString (inlineQuery, keywords)
        | _ -> Search All, inlineQuery

let private matcher: Map<Service, ServiceSearchArgument -> Async<ServiceSearchResult>> =
    Map [ (YouTube, youTubeSearch)
          (SoundCloud, soundCloudSearch) ]


let private searchInAllServicesAsync arg =
    async {

        let! values =
            matcher
            |> Seq.map (fun pair ->
                async {
                    let! res = pair.Value(arg)

                    return
                        res
                        |> Result.bind (fun s ->
                            s
                            |> Seq.map (fun x ->
                                { x with
                                      Title = $"[{pair.Key}] {x.Title}" })
                            |> Ok)
                })
            |> Async.Parallel

        let (_, q, token) = arg

        return
            values
            |> Seq.collect (fun x ->
                match x with
                | Ok ok -> ok
                | Error error ->
                    Async.Start(sendErrorToDeveloperAsync (q, error), token)
                    Seq.empty)
            |> Seq.sortByDescending (fun x -> x.PlayCount)
            |> Ok
    }


let private dispatchSearchAsync search arg =
    arg
    |> match search with
       | All -> searchInAllServicesAsync
       | specific -> matcher.Item(specific)

let private sendResultsAsync offset (inlineQuery: InlineQuery) (result: ServiceSearchResult) =
    match result with
    | Ok success ->
        success
        |> Seq.indexed
        |> Seq.map (searchResultsMapper offset)
        |> (fun x -> curryingAnswerSender inlineQuery.Id (offset + Seq.length x) x)
    | Error error -> sendErrorToDeveloperAsync (inlineQuery, error)

let private sendWrongCommandResponseAsync (queryId: string, command) =
    let message = sprintf "Wrong command: %s." command

    let article =
        InlineQueryResultArticle("0", "Wrong command", InputTextMessageContent(message))

    article.Description <- message

    bot.AnswerInlineQueryAsync(queryId, [ article ])
    |> Async.AwaitTask

let private dispatchAsync (inlineQuery: InlineQuery, token) =
    match readQuery inlineQuery with
    | (Help, q) -> helpAsync (q, token)
    | (Search s, modifiedInlineQuery) ->
        async {
            let offset =
                if String.IsNullOrEmpty(modifiedInlineQuery.Offset)
                then 0
                else modifiedInlineQuery.Offset |> int

            let! res =
                (offset, modifiedInlineQuery, token)
                |> dispatchSearchAsync s

            do! sendResultsAsync offset inlineQuery res
        }
    | (Wrong w, q) -> sendWrongCommandResponseAsync (q.Id, w)

let updateAsyncHandler token (update: Update) =
    fun (ctx: HttpContext) ->
        async {
            let inlineQuery = update.InlineQuery

            do! (inlineQuery, token)
                |> if inlineQuery.Query.Length >= minimumKeywordLength
                   then dispatchAsync
                   else helpAsync

            return! OK String.Empty ctx
        }
