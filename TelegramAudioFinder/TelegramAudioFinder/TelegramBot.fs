module TelegramAudioFinder.TelegramBot

open Telegram.Bot
open Utils

let bot = TelegramBotClient(applicationConfig.Telegram.Token)