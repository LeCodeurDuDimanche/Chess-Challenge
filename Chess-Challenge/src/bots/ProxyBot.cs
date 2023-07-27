using ChessChallenge.API;
using System;
public class ProxyBot<BotClass> : IChessBot where BotClass: IChessBot
{
    private BotClass proxyBot;
    public ProxyBot() {
        proxyBot = (BotClass) Activator.CreateInstance(typeof(BotClass));
    }
    public Move Think(Board board, Timer timer)
    {
        return proxyBot.Think(board, timer);
    }
}