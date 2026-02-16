using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands.Kasino;

public class ShopHelpCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^shop help$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!beg to beg for a loan";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromSeconds(120)
    };

    private const string KasinoShopHelpLink = "";
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        
        //just gonna link to an html hosted on iddos
    }
}

public class ListCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^list (?<choice>loans|assets|investments|rtp)$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!beg to beg for a loan";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromSeconds(120)
    };

    private const string KasinoShopHelpLink = "";
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);

        if (!arguments.TryGetValue("choice", out var choice))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !list <loans|assets|investments|rtp>", true, autoDeleteAfter: cleanupDelay);
            return;
        }

        switch (choice.Value)
        {
            case "loans":
                await botInstance.BotServices.KasinoShop.PrintLoansList(gambler);
                break;
            case "assets":
                break;
            case "investments":
                break;
            case "rtp":
                await botInstance.BotServices.KasinoShop.PrintRtp(gambler);
                break;
        }
    }
}

public class BegCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^beg$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!beg to beg for a loan";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromSeconds(120)
    };
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        await botInstance.BotServices.KasinoShop.ProcessBeg(gambler);
    }
}

public class LoanCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^loan (?<reciever>\d+) (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^loans (?<action>clear)$", RegexOptions.IgnoreCase),
        new Regex(@"^loan$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!beg to beg for a loan";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        if (!arguments.TryGetValue("reciever", out var reciever) || !arguments.TryGetValue("amount", out var amount)) //list users active loans
        {
            await botInstance.BotServices.KasinoShop.PrintLoansList(gambler);
            //await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !loan <id> <amount>", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        int id = Convert.ToInt32(reciever.Value);
        if (!botInstance.BotServices.KasinoShop.Gambler_Profiles.ContainsKey(id))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, could not find a kasino shop profile for {id}.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        await botInstance.BotServices.KasinoShop.ProcessLoan(id, Convert.ToDecimal(amount.Value), gambler, gambler.Id);
    }
}

public class RepaymentCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^repay (?<reciever>\d+) (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^repay$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!beg to beg for a loan";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        if (!arguments.TryGetValue("reciever", out var reciever) || !arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !repay <id> <amount>", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        int id = Convert.ToInt32(reciever.Value);
        if (!botInstance.BotServices.KasinoShop.Gambler_Profiles.ContainsKey(id))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, could not find a kasino shop profile for {id}.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        decimal amountToPay = Convert.ToDecimal(amount.Value);

        await botInstance.BotServices.KasinoShop.ProcessRepayment(gambler, user, id, amountToPay);
    }
}


public class ShopAssetsCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^shop assets (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop assets$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!shop to get a list of shop commands";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        
        
    }
}

public class ShopInvestmentsCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^investments$", RegexOptions.IgnoreCase),
        new Regex(@"^buy investments (?<num>\d+) (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^buy investments (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^buy investments$", RegexOptions.IgnoreCase),
        new Regex(@"^shop investments (?<num>\d+) (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop investments (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop investments$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!shop investments to look at the investments for sale. 1 2 or 3 and an optional amount at the end to buy the investment (default you will buy 1)";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);

        if (!arguments.TryGetValue("num", out var num))
        {
            await botInstance.BotServices.KasinoShop.PrintInvestmentMarket(gambler);
            return;
        }
        int item = Convert.ToInt32(num.Value);
        decimal investment;
        if (!arguments.TryGetValue("amount", out var amount))
        {
            switch (item)
            {
                case 1: investment = 300000; break;
                case 2: investment = 10000; break;
                case 3: investment = 100000000; break;
                default: await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, invalid pick, must pick 1, 2, or 3.", true, autoDeleteAfter: cleanupDelay); return;
            }
        }
        else investment = Convert.ToDecimal(amount.Value);
        
        await botInstance.BotServices.KasinoShop.ProcessInvestment(gambler, item, investment);
    }
}

public class ShopStakeCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^stake$", RegexOptions.IgnoreCase),
        new Regex(@"^shop stake$", RegexOptions.IgnoreCase),
        new Regex(@"^stake (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop stake (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
    ];
    public string? HelpText => "!shop investments to look at the investments for sale. 1 2 or 3 and an optional amount at the end to buy the investment (default you will buy 1)";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);

        if (!arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, stake your crypto to earn a small amount of interest every day. Minimum stake time: 1 week. !stake <amount>", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        await botInstance.BotServices.KasinoShop.ProcessStake(gambler, Convert.ToDecimal(amount.Value));
    }
}


public class ShopDrugsCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^shop drugs (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop drugs$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!shop to get a list of shop commands";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        
    }
}


public class ShopDepositCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^deposit (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^depo (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^deposit$", RegexOptions.IgnoreCase),
        new Regex(@"^depo$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!shop to get a list of shop commands";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        if (!arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.BotServices.KasinoShop.PrintBalance(gambler);
        }
        decimal depo = Convert.ToDecimal(amount!.Value);
        await botInstance.BotServices.KasinoShop.ProcessDeposit(gambler, depo);
    }
}

public class ShopWtihdrawCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^withdraw (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^withdraw$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!shop to get a list of shop commands";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        if (!arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you can withdraw from your kasino balance of {await gambler.Balance.FormatKasinoCurrencyAsync()}. You must withdraw a minimum of {await 5000m.FormatKasinoCurrencyAsync()}.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        decimal withdraw = Convert.ToDecimal(amount!.Value);
        if (withdraw < 5000)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you must withdraw a minimum of {await 5000m.FormatKasinoCurrencyAsync()}.", true, autoDeleteAfter: cleanupDelay);
        }
        await botInstance.BotServices.KasinoShop.ProcessDeposit(gambler, withdraw);
    }
}

public static class GlobalShopFunctions
{
    public static async Task CheckProfile(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler) //checks if the user trying to run the command has a profile. if they do not, creates a profile for them
    {
        if (!botInstance.BotServices.KasinoShop!.Gambler_Profiles.ContainsKey(user.KfId))
        {
            await botInstance.BotServices.KasinoShop.CreateProfile(user.KfId, gambler.Id);
            await botInstance.SendChatMessageAsync($"Created kasino shop profile for {user.FormatUsername()}({user.KfId})", true);
        }
        
    }
}
