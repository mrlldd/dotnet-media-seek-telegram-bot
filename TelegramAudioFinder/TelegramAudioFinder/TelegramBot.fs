module TelegramAudioFinder.TelegramBot

open System
open System.Net
open Microsoft.FSharp.Collections
open Newtonsoft.Json
open SoundCloud.Api
open Suave
open Successful
open Telegram.Bot
open Telegram.Bot.Types
open Telegram.Bot.Types.InlineQueryResults
open TelegramAudioFinder.YouTubeSearch
open TelegramAudioFinder.Utils
open TelegramAudioFinder.Services.Utils

let bot =
    TelegramBotClient(applicationConfig.Telegram.Token)

let private curryingAnswerSender queryId offset items =
    bot.AnswerInlineQueryAsync
        (queryId,
         items,
         nextOffset = (offset |> string),
         cacheTime = cacheTimeout.Seconds,
         cancellationToken = cts.Token)
    |> Async.AwaitTask

[<Literal>]
let private thumbSideSize = 100

let searchResultsMapper (offset: int) (index, item: UnifiedSearchResult) =
    let res =
        InlineQueryResultArticle
            ((index + offset) |> string, item.Title |> WebUtility.HtmlDecode, InputTextMessageContent(item.Url))

    res.ThumbUrl <- item.ThumbUrl
    res.Description <- item.Description
    res.ThumbHeight <- thumbSideSize
    res.ThumbWidth <- thumbSideSize
    res :> InlineQueryResultBase

let private sendErrorToDeveloperAsync (inlineQuery, error) =
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
    "/?h or /?help for help\n"
    + "/?y or /?youtube for search with YouTube\n"
    + "/?s or /?soundcloud for search with SoundCloud\n"
    + "/?a or /?all for search with all available services\n"
    + "Example: /?a astrothunder\n"
    + "Query without arguments will perform search in all available services by default.\n"
    + $"Minimum keyword length: {minimumKeywordLength}"

let private helpAsync (inlineQuery: InlineQuery, token) =
    let article =
        InlineQueryResultArticle("0", "Help", InputTextMessageContent(helpDescription))

    article.Description <- helpDescription

    bot.AnswerInlineQueryAsync(inlineQuery.Id, [ article ], 0, cancellationToken = token)
    |> Async.AwaitTask

type private Service =
    | YouTube
    | SoundCloud
    | All

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
        | "/?h"
        | "/?help" -> Help, inlineQuery
        | command when command.StartsWith("/?") ->
            match readSearchQuery command with
            | Wrong w -> Wrong w, inlineQuery
            | search -> search, modifyQueryString (inlineQuery, keywords)
        | _ -> Search All, inlineQuery

let private dispatchSearchAsync (search, offset, query: InlineQuery, token) =
    match search with
    | YouTube
    | All -> youTubeSearch (offset, query, token)
    | SoundCloud -> async { return Ok Seq.empty }

let private sendResultsAsync offset (inlineQuery: InlineQuery) (result: Result<seq<UnifiedSearchResult>, exn>) =
    match result with
    | Ok success ->
        success
        |> Seq.indexed
        |> Seq.map (searchResultsMapper offset)
        |> curryingAnswerSender inlineQuery.Id (offset + pageLength)
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
                match String.IsNullOrEmpty(modifiedInlineQuery.Offset) with
                | true -> 0
                | false -> modifiedInlineQuery.Offset |> int

            let! res = dispatchSearchAsync (s, offset, modifiedInlineQuery, token)
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
