namespace TelegramAudioFinder

type TelegramConfig = {
    Token: string
    WebhookUrl: string
    MaxConnections: int
}

type ApplicationConfig = {
    Telegram: TelegramConfig
}