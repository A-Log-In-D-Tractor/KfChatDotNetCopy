using System.Text.Json;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Settings;
using NLog;
using StackExchange.Redis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;
using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Services;

public class KasinoShop
{
    private static RandomShim<StandardRng> _rand = RandomShim.Create(StandardRng.Create());
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private IDatabase? _redisDb;
    public static ChatBot BotInstance = null!;
    public int[]? activeLoanIds = null;
    public Dictionary<int, KasinoShopProfile> Gambler_Profiles = new(); //list of all profiles, accesesd via kf user id
    public Dictionary<int, CancellationTokenSource> User_Tokens = new();
    
    public KasinoShop(ChatBot kfChatBot)
    {
        BotInstance = kfChatBot;
        LoadProfiles();
        foreach (var userId in Gambler_Profiles.Keys)
        {
            User_Tokens.Add(userId, new CancellationTokenSource());
        }
    }

    public async void LoadProfiles()
    {
        if (_redisDb == null) throw new InvalidOperationException("Kasino shop service isn't initialized");
        var json = await _redisDb.StringGetAsync($"Shop.Profiles.State");
        var json2 = await _redisDb.StringGetAsync($"Shop.Loans.State");
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var options = new JsonSerializerOptions{IncludeFields = true};
            Gambler_Profiles = JsonSerializer.Deserialize<Dictionary<int, KasinoShopProfile>>(json.ToString(), options) ??
                          throw new InvalidOperationException();
            
        }
        catch (Exception e)
        {
            _logger.Error(e);
            _logger.Error("Potentially failed to deserialize active mines games in GetSavedGames() in KasinoMines in Services");
            Gambler_Profiles = new Dictionary<int, KasinoShopProfile>();
        }
    }

    public async Task SaveProfiles()
    {
        if (_redisDb == null) throw new InvalidOperationException("Kasino mines service isn't initialized");
        var options = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = false
        };
        var json = JsonSerializer.Serialize(Gambler_Profiles, options);
        await _redisDb.StringSetAsync($"Shop.Profiles.State", json, null, When.Always);
    }

    public async Task ResetProfiles()
    {
        Gambler_Profiles = new Dictionary<int, KasinoShopProfile>();
        await SaveProfiles();
    }


    public async Task PrintBalance(GamblerDbModel gambler)
    {
        await BotInstance.SendChatMessageAsync($"{Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
    }
    
    public async Task ResetLoans(UserDbModel instanceCaller)
    {
        foreach (var key in Gambler_Profiles.Keys)
        {
            Gambler_Profiles[key].Loans.Clear();
        }
        await SaveProfiles();
    }

    public void ClearInterest(UserDbModel instanceCaller)
    {
        int kfId = instanceCaller.KfId;
        foreach (var loanKey in Gambler_Profiles[kfId].Loans.Keys)
        {
            var loan = Gambler_Profiles[kfId].Loans[loanKey];

            if (loan.payoutAmount > loan.amount)
            {
                Gambler_Profiles[kfId].Loans.Remove(loanKey);
                Gambler_Profiles[loan.payableToKf].Loans.Remove(loanKey);
                loan.payoutAmount = loan.amount;
                Gambler_Profiles[kfId].Loans.Add(loanKey, loan);
                Gambler_Profiles[loan.payableToKf].Loans.Add(loanKey, loan);
            }
            //else you already paid the interest part of the loan so nothing to clear
        }
    }

    public decimal GetCurrentCrackPrice(GamblerDbModel gambler)
    {
        return CrackPrice * Gambler_Profiles[gambler.User.KfId].CrackCounter;
    }
    
    public async Task PrintDrugMarket(GamblerDbModel gambler)
    {
        int cc = Gambler_Profiles[gambler.User.KfId].CrackCounter;
        List<string> drugs = new();
        drugs.Add($"1. Crack: {await (CrackPrice * cc).FormatKasinoCurrencyAsync()} per dose");
        drugs.Add($"2. Weed: {await WeedPricePerHour.FormatKasinoCurrencyAsync()} per hour");
        if (Gambler_Profiles[gambler.User.KfId].FloorNugs > 0)
        {
            drugs.Add($"3. Floor Nugs: {Gambler_Profiles[gambler.User.KfId].FloorNugs}");
        }
    }

    public bool CheckWagerReq(GamblerDbModel gambler)
    {
        if (Gambler_Profiles[gambler.User.KfId].SponsorWagerLock[0] >
            Gambler_Profiles[gambler.User.KfId].SponsorWagerLock[1]) return false;
        return true;
    }

    public decimal RemainingWagerReq(GamblerDbModel gambler)
    {
        return Gambler_Profiles[gambler.User.KfId].SponsorWagerLock[0] - Gambler_Profiles[gambler.User.KfId].SponsorWagerLock[1];
    }
    
    public async Task ProcessDrugUse(GamblerDbModel gambler, decimal amount, int drug)
    {
        Dictionary<int, string> drugs = new()
        {
            {1, "Crack"},
            {2, "Weed"},
            {3, "Floor Nugs"}
        };
        if (drug != 3)
        {
            if (Gambler_Profiles[gambler.User.KfId].Balance()[0] < amount)
            {
                await BotInstance.SendChatMessageAsync(
                    $"{gambler.User.FormatUsername()}, you can't afford to buy {await amount.FormatKasinoCurrencyAsync()} worth of {drugs[drug]}. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                return;
            }
        }
        else
        {
            if (Gambler_Profiles[gambler.User.KfId].FloorNugs < amount)
            {
                await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you only have {Gambler_Profiles[gambler.User.KfId].FloorNugs} floor nugs, so that's all you could smoke right now.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                amount = Gambler_Profiles[gambler.User.KfId].FloorNugs;
            }
        }

        if (drug == 1)
        {
            _ = Gambler_Profiles[gambler.User.KfId].SmokeCrack();
        }
        else if (drug == 2)
        {
            TimeSpan weedDuration = TimeSpan.FromHours((double)(amount / WeedPricePerHour));
            _ = Gambler_Profiles[gambler.User.KfId].SmokeWeed(weedDuration);
        }
        else
        {
            TimeSpan dur = TimeSpan.FromMinutes(6 * (int)amount);
            _ = Gambler_Profiles[gambler.User.KfId].SmokeWeed(dur);
        }
    }

    public async Task<bool> ProcessLoan(int receiverKfId, decimal amount, GamblerDbModel gUser, int senderGamblerId)
    {
        var sender = gUser.User;
        //check if the person they tried to loan to is able to get a loan
        await using var db = new ApplicationDbContext();
        var targetUser = await db.Users.FirstOrDefaultAsync(u => u.KfId == receiverKfId);
        if (targetUser == null)
        {
            await BotInstance.SendChatMessageAsync($"{sender.FormatUsername()}, user with KF ID {receiverKfId} not found.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return false;
        }
        if (!Gambler_Profiles[receiverKfId].IsLoanable)
        {
            await BotInstance.SendChatMessageAsync(
                $"{sender.FormatUsername()}, {targetUser.FormatUsername()} is not loanable, they need to beg for a loan first.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return false;
        }
        //check if the loaner has enough crypto to send loan
        if (Gambler_Profiles[sender.KfId].Balance()[1] < amount)
        {
            await BotInstance.SendChatMessageAsync($"{sender.FormatUsername()}, you don't have enough crypto to loan {targetUser.FormatUsername()}. {amount}. {await Gambler_Profiles[sender.KfId].FormatBalanceAsync()}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return false;
        }

        Random rand = new Random();
        int loanId = (int)(1000000000 * rand.NextDouble());
        if (activeLoanIds == null) activeLoanIds = new int[1];
        else
        {
            int[] newLoans = new int[activeLoanIds.Length + 1];
            activeLoanIds.CopyTo(newLoans, 1);
            newLoans[0] = loanId;
            activeLoanIds = newLoans;
        }
        await using var gamblerdb = new ApplicationDbContext();
        var targetgambler = await gamblerdb.Gamblers.FirstOrDefaultAsync(g => g.User.KfId == Gambler_Profiles[receiverKfId].GamblerId);
        if (targetgambler == null)
        {
            await BotInstance.SendChatMessageAsync($"{sender.FormatUsername()}, gambler profile for user with KF ID {receiverKfId} not found.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return false;
        }
        //public Loan(decimal amount, int payableToGambler, int payableToKf, int recieverGambler, int recieverKf, int Id)
        Loan loan = new Loan(amount, senderGamblerId, sender.Id, targetgambler.Id, receiverKfId, loanId, sender);
        //take from senders crypto balance and deposit into receivers kasino balance
        Gambler_Profiles[sender.KfId].ModifyBalance(-amount);
        await Money.ModifyBalanceAsync(targetgambler.Id, amount, TransactionSourceEventType.Loan);
        Gambler_Profiles[sender.KfId].Loans.Add(loanId, loan);
        Gambler_Profiles[receiverKfId].Loans.Add(loanId, loan);
        Gambler_Profiles[receiverKfId].OutstandingLoanBalance += amount;
        Gambler_Profiles[receiverKfId].IsLoanable = false;
        Gambler_Profiles[receiverKfId].KreditScore -= Convert.ToInt32(amount / 2);
        await SaveProfiles();
        return true;
    }

    
    public async Task ProcessRepayment(GamblerDbModel payerGambler, UserDbModel payer, int payeeKfId, decimal amount) //loans can be repaid from crypto balance and kasino balance. it prefers to take from your crypto balance first if you have any
    {
        decimal payerTotalBalance = payerGambler.Balance + Gambler_Profiles[payer.KfId].Balance()[0];
        if (payerTotalBalance < amount)
        {
            await BotInstance.SendChatMessageAsync($"{payer.FormatUsername()}, you don't have enough to repay {await amount.FormatKasinoCurrencyAsync()}. Your total balance: {await payerTotalBalance.FormatKasinoCurrencyAsync()}");
            return;
        }

        
        int loanId = -1;
        //find the loan
        foreach (var loan in Gambler_Profiles[payeeKfId].Loans.Values)
        {
            if (loan.payableToKf == payer.KfId)
            {
                loanId = loan.Id;
                break;
            }
        }

        if (loanId == -1)
        {
            await BotInstance.SendChatMessageAsync($"{payer.FormatUsername()}, you don't have a loan with {payeeKfId}.");
            return;
        }
        //compare the amount paid to the amount of the loan
        if (amount >= Gambler_Profiles[payer.KfId].Loans[loanId].payoutAmount)
        {
            //if the amount is more or equal, clear the loan when you pay, and give the payer credit score bonus for repaying the loan
            amount = Gambler_Profiles[payer.KfId].Loans[loanId].payoutAmount;
            Gambler_Profiles[payer.KfId].KreditScore += Convert.ToInt32(Gambler_Profiles[payer.KfId].Loans[loanId].amount * 3 / 4);
        }
        else if (amount < Gambler_Profiles[payeeKfId].Loans[loanId].payoutAmount)
        {
            Gambler_Profiles[payeeKfId].Loans[loanId].payoutAmount -= amount;
        }
        //split the amount from crypto and kasino balance as available
        decimal takeFromCrypto = 0;
        decimal takeFromKasino = 0;

        if (amount > Gambler_Profiles[payeeKfId].Balance()[0])
        {
            //if the amount is more than the amount of crypto you have, take from kasino balance as well
            takeFromCrypto = Gambler_Profiles[payeeKfId].Balance()[0];
            takeFromKasino = amount - takeFromCrypto;
        }
        else takeFromCrypto = amount;
        //process the payments
        Gambler_Profiles[payer.KfId].ModifyBalance(-takeFromCrypto);
        await Money.ModifyBalanceAsync(payerGambler.Id, -takeFromKasino, TransactionSourceEventType.Loan);
        Gambler_Profiles[payeeKfId].ModifyBalance(amount);
        await SaveProfiles();
    }

    public async Task PrintLoansList(GamblerDbModel gambler)
    {
        var user = gambler.User;
        string message = $"{user.FormatUsername()}";
        var msg = await BotInstance.SendChatMessageAsync($"{message}", true);
        await BotInstance.WaitForChatMessageAsync(msg);
        if (Gambler_Profiles[user.KfId].Loans.Count == 0)
        {
            message += " is debt free!";
            await BotInstance.KfClient.EditMessageAsync(msg.ChatMessageId!.Value, message);
            await Task.Delay(TimeSpan.FromSeconds(10));
            await BotInstance.KfClient.DeleteMessageAsync(msg.ChatMessageId.Value);
            return;
        }

        foreach (var loan in Gambler_Profiles[user.KfId].Loans.Values)
        {
            message += $"[br]{await loan.ToStringAsync(gambler.User.KfId)}";
            await BotInstance.KfClient.EditMessageAsync(msg.ChatMessageId!.Value, message);
            await Task.Delay(10);
        }
        await Task.Delay(TimeSpan.FromSeconds(10));
        await BotInstance.KfClient.DeleteMessageAsync(msg.ChatMessageId!.Value);
    }

    
    public async Task ProcessBeg(GamblerDbModel gambler)
    {
        var user = gambler.User;
        Gambler_Profiles[user.KfId].Beg(user);
        await SaveProfiles();
    }

    public async Task ProcessWithdraw(GamblerDbModel gambler, decimal amount)
    {
        int kfId = gambler.User.KfId;
        if (amount > gambler.Balance)
        {
            await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you don't have enough to withdraw {await amount.FormatKasinoCurrencyAsync()}. Balance: {await gambler.Balance.FormatKasinoCurrencyAsync()}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }

        Gambler_Profiles[gambler.User.KfId].Withdraw(amount);
        var newBalance = await Money.ModifyBalanceAsync(gambler.Id, amount, TransactionSourceEventType.Withdraw);
        await SaveProfiles();
        await BotInstance.SendChatMessageAsync(
            $"{gambler.User.FormatUsername()}, you withdrew {await amount.FormatKasinoCurrencyAsync()} to your crypto balance. Kasino Balance: {await newBalance.FormatKasinoCurrencyAsync()} | {await Gambler_Profiles[kfId].FormatBalanceAsync()}",
            true, autoDeleteAfter: TimeSpan.FromSeconds(10));
        
    }

    public async Task ProcessDeposit(GamblerDbModel gambler, decimal amount)
    {
        int kfId = gambler.User.KfId;
        if (amount > Gambler_Profiles[kfId].Balance()[0])
        {
            await BotInstance.SendChatMessageAsync(
                $"{gambler.User.FormatUsername()}, you don't have enough crypto to deposit {await amount.FormatKasinoCurrencyAsync()}. {await Gambler_Profiles[kfId].FormatBalanceAsync()}");
            return;
        }
        var newBalance = await Money.ModifyBalanceAsync(gambler.Id, amount, TransactionSourceEventType.Deposit);
        Gambler_Profiles[kfId].Deposit(amount);
        await SaveProfiles();
        await BotInstance.SendChatMessageAsync(
            $"{gambler.User.FormatUsername()}, you deposited {await amount.FormatKasinoCurrencyAsync()} to your kasino balance. Kasino Balance: {await newBalance.FormatKasinoCurrencyAsync()} | {await Gambler_Profiles[kfId].FormatBalanceAsync()}",
            true, autoDeleteAfter: TimeSpan.FromSeconds(10));

    }

    public async Task ProcessInvestment(GamblerDbModel gambler, int item, decimal amount)
    {
        if (amount > Gambler_Profiles[gambler.User.KfId].Balance()[1])
        {
            await BotInstance.SendChatMessageAsync(
                $"{gambler.User.FormatUsername()}, you can't afford this investment. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }

        if (item < 1 || item > 3)
        {
            await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, invalid investment choice. 1 - gold, 2 - silver, 3 - house.",true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }

        int id = Money.GetRandomNumber(gambler, 0, 999999999);
        int counter = 0;
        for (int i = 0; i < Gambler_Profiles[gambler.User.KfId].Assets.Count; i++)
        {
            if (Gambler_Profiles[gambler.User.KfId].Assets.Values.ElementAt(i).Id == id)
            {
                id = Money.GetRandomNumber(gambler, 0, 999999999);
                i = -1;
                counter++;
                if (counter > 10000)
                {
                    await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, failed to generate new item ID after 10000 attempts", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                    throw new Exception("Failed to generate unused item ID after 10000 attempts");
                }
            }
        }

        string str;
        switch (item)
        {
            case 1:
                str = Money.GetRandomDouble(gambler) < 0.5 ? "chain" : "coin";
                Investment newGold = new Investment(id, amount, GoldInterestRange, InvestmentType.Gold, $"Gold {str}");
                Gambler_Profiles[gambler.User.KfId].Assets.Add(id, newGold);
                break;
            case 2:
                str = Money.GetRandomDouble(gambler) < 0.5 ? "chain" : "coin";
                Investment newSilver = new Investment(id, amount, SilverInterestRange, InvestmentType.Silver, $"Silver {str}");
                Gambler_Profiles[gambler.User.KfId].Assets.Add(id, newSilver);
                break;
            case 3:
                Investment newHouse = new Investment(id, amount, HouseInterestRange, InvestmentType.House, "House");
                Gambler_Profiles[gambler.User.KfId].Assets.Add(id, newHouse);
                break;
        }

        await SaveProfiles();
    }

    public async Task ProcessCarPurchase(GamblerDbModel gambler, int carId)
    {
        //civic audi bentley bmw
        foreach (var asset in Gambler_Profiles[gambler.User.KfId].Assets.Values)
        {
            if (asset is Car c)
            {
                await BotInstance.SendChatMessageAsync(
                    $"{gambler.User.FormatUsername()}, you can't buy a car you already have one: {c}.", true,
                    autoDeleteAfter: TimeSpan.FromSeconds(10));
                return;
            }
        }
        var car = DefaultCars.ElementAt(carId).Value;
        await car.SetId(gambler);
        Gambler_Profiles[gambler.User.KfId].Assets.Add(car.Id, car);
        await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you bought {car}", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
        await SaveProfiles();
    }

    public async Task ProcessWorkJob(GamblerDbModel gambler)
    {
        bool hasCar = false;
        int carId = -1;
        foreach (var assetKey in Gambler_Profiles[gambler.User.KfId].Assets.Keys)
        {
            if (Gambler_Profiles[gambler.User.KfId].Assets[assetKey] is Car car)
            {
                carId = assetKey;
                hasCar = true;
                break;
            }
        }

        if (!hasCar)
        {
            await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you don't have a car to get a job with.", true, autoDeleteAfter:TimeSpan.FromSeconds(10));
            return;
        }

        await ((Car)(Gambler_Profiles[gambler.User.KfId].Assets[carId])).ProcessWorkJob(gambler);
        await SaveProfiles();
    }
    public async Task ProcessWagerTracking(GamblerDbModel gambler, WagerGame game, decimal amount, decimal net, decimal newBalance)
    {
        Gambler_Profiles[gambler.User.KfId].Tracker.AddWager(game, amount, net);
        if (newBalance < 1 && Gambler_Profiles[gambler.User.KfId].SponsorWagerLock[0] > 0) //if you ran out of money after that gamble reset your wager lock
        {
            Gambler_Profiles[gambler.User.KfId].SponsorWagerLock = new decimal[] { 0, 0 };
        }
        await SaveProfiles();
    }
    
    public async Task ProcessJuicerOrRainTracking(GamblerDbModel sender, List<GamblerDbModel> recievers, decimal amountPerReciever)
    {
        Gambler_Profiles[sender.User.KfId].Tracker.AddWithdrawal(amountPerReciever * recievers.Count);
        foreach (var reciever in recievers)
        {
            if (Gambler_Profiles.ContainsKey(reciever.User.KfId)) Gambler_Profiles[reciever.User.KfId].Tracker.AddDeposit(amountPerReciever);
        }

        await SaveProfiles();
    }
    
    public async Task ProcessStake(GamblerDbModel gambler, decimal amount)
    {
        //check if they have enough crypto for the stake
        if (Gambler_Profiles[gambler.User.KfId].Balance()[1] < amount)
        {
            await BotInstance.SendChatMessageAsync(
                $"{gambler.User.FormatUsername()}, you don't have enough crypto to stake {await amount.FormatKasinoCurrencyAsync()}. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}",true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        int id = Money.GetRandomNumber(gambler, 0, 999999999);
        int counter = 0;
        for (int i = 0; i < Gambler_Profiles[gambler.User.KfId].Assets.Count; i++)
        {
            if (Gambler_Profiles[gambler.User.KfId].Assets.Values.ElementAt(i).Id == id)
            {
                id = Money.GetRandomNumber(gambler, 0, 999999999);
                i = -1;
                counter++;
                if (counter > 10000)
                {
                    await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, failed to generate new item ID after 10000 attempts", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                    throw new Exception("Failed to generate unused item ID after 10000 attempts");
                }
            }
        }

        var stake = new Investment(id, amount, CryptoStakeInterestRange, InvestmentType.Stake, "Stake");
        Gambler_Profiles[gambler.User.KfId].Assets.Add(id, stake);
        Gambler_Profiles[gambler.User.KfId].ModifyBalance(-amount);
        await BotInstance.SendChatMessageAsync(
            $"{gambler.User.FormatUsername()}, you staked {await amount.FormatKasinoCurrencyAsync()} crypto.[br] {Gambler_Profiles[gambler.User.KfId].Assets[id]} {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}");
        await SaveProfiles();
    }
    
    public async Task ProcessAssetSale(GamblerDbModel gambler, int assetId)
    {
        if (!Gambler_Profiles[gambler.User.KfId].Assets.ContainsKey(assetId))
        {
            await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you don't have any assets with id {assetId}.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        var asset = Gambler_Profiles[gambler.User.KfId].Assets[assetId];
        int cooldown;
        if (asset is Investment inv)
        {
            switch (inv.investment_type)
            {
                case InvestmentType.Gold or InvestmentType.Silver:
                    cooldown = (DateTime.UtcNow - inv.acquired).Days;
                    if (cooldown < 5)
                    {
                        await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you can't sell your {inv.investment_type} investment yet, It's been less than 5 days since you bought it. {cooldown} days until it arrives.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                        return;
                    }

                    break;
                case InvestmentType.Stake:
                    cooldown = (DateTime.UtcNow - inv.acquired).Days;
                    if (cooldown < 7)
                    {
                        await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, you can't sell your Stake yet, {cooldown} days until it unlocks.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                        return;
                    }

                    break;
            }
        }
        else if (asset is Smashable smash)
        {
            await BotInstance.SendChatMessageAsync($"{gambler.User.FormatUsername()}, nobody wants to buy your shitty {smash}.", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        
        
        
        Gambler_Profiles[gambler.User.KfId].Assets.Remove(assetId);
        Gambler_Profiles[gambler.User.KfId].ModifyBalance(asset.GetCurrentValue());
        await BotInstance.SendChatMessageAsync(
            $"{gambler.User.FormatUsername()} sold {asset}. {await Gambler_Profiles[gambler.User.KfId].FormatBalanceAsync()}");
        await SaveProfiles();
    }

    public async Task PrintShoeMarket(GamblerDbModel gambler)
    {
        
    }

    public async Task PrintSkinMarket(GamblerDbModel gambler)
    {
        
    }

    public async Task UpdateGambler(GamblerDbModel gambler)
    {
        //if someone abandons their gambler profile they can do !shop update gambler to update their gambler profile
        Gambler_Profiles[gambler.User.KfId].GamblerId = gambler.Id;
        await SaveProfiles();
    }

    public async Task UpdateProfileId(GamblerDbModel gambler)
    {
        //if someone gets their account fucked with by null, assuming their gambler id stays the same
        foreach (var key in Gambler_Profiles.Keys)
        {
            if (Gambler_Profiles[key].GamblerId == gambler.Id)
            {
                Gambler_Profiles[key].ID = gambler.User.KfId;
            }
        }
        await SaveProfiles();
    }


    public async Task PrintRtp(GamblerDbModel gambler)
    {
        var rtp = Gambler_Profiles[gambler.User.KfId].Tracker.GetRtp();
        await BotInstance.SendChatMessageAsync(rtp, true, autoDeleteAfter: TimeSpan.FromSeconds(10));
    }

    public async Task PrintAssets(GamblerDbModel gambler)
    {
        string str = $"{gambler.User.FormatUsername()}'s assets:[br]";
        int counter = 1;
        bool hasAssets = false;
        foreach (var asset in Gambler_Profiles[gambler.User.KfId].Assets.Values)
        {
            str += $"{counter}: {asset}[br]";
            counter++;
            hasAssets = true;
        }
        if (!hasAssets) str = $"{gambler.User.FormatUsername()}, you don't have any assets.";
        await BotInstance.SendChatMessageAsync(str, true, autoDeleteAfter: TimeSpan.FromSeconds(10));
    }

    public async Task PrintInvestments(GamblerDbModel gambler)
    {
        string str = $"{gambler.User.FormatUsername()}'s investments:[br]";
        int counter = 1;
        bool hasInvestments = false;
        foreach (var asset in Gambler_Profiles[gambler.User.KfId].Assets.Values)
        {
            if (asset is Investment i)
            {
                str += $"{counter}: {i}[br]";
                counter++;
                hasInvestments = true;
            }
        }
        if (!hasInvestments) str = $"{gambler.User.FormatUsername()}, you don't have any investments.";
        await BotInstance.SendChatMessageAsync(str, true, autoDeleteAfter: TimeSpan.FromSeconds(10));
    }
    
    public async Task CreateProfile(GamblerDbModel gambler)
    {
        await BotInstance.SendChatMessageAsync($"Creating profile for {gambler.User.FormatUsername()}...", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
        if (Gambler_Profiles.ContainsKey(gambler.User.KfId))
        {
            throw new Exception("Attempted to create a new profile for someone who seems to already have a profile?");
        }
        var profile = new KasinoShopProfile(gambler);
        Gambler_Profiles.Add(profile.ID, profile);
        await SaveProfiles();
        
        
    }
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    public class KasinoShopProfile
    {
        public int ID { get; set; }
        public int GamblerId { get; set; }
        public string name;
        private decimal CryptoBalance;
        public decimal OutstandingLoanBalance;
        public Dictionary<int, Asset> Assets;
        public Dictionary<int, Loan> Loans = new();
        public decimal[] SponsorWagerLock = new decimal[2]; //[0] is how much you've wagered against your wager requirement, [1] is the wager requirement
        public decimal HouseEdgeModifier = 0;
        public int CrackCounter = 0;
        public int FloorNugs = 0;
        public DateTime LastSmokedCrack = DateTime.MinValue;
        public bool IsSponsored;
        public bool IsWeeded;
        public bool IsCracked;
        public bool IsInWithdrawal;
        public bool IsLoanable;
        private SkinMarket? _sMarket = null;
        private CancellationTokenSource CrackToken = new();
        private CancellationTokenSource WeedToken = new();
        private CancellationTokenSource BegToken = new();
        private TimeSpan WeedTimer = TimeSpan.FromSeconds(0); //time remaining on your weed buff
        private TimeSpan CrackTimer = TimeSpan.FromSeconds(0);
        public int KreditScore;
        public StatTracker Tracker;
        
        public KasinoShopProfile(GamblerDbModel gambler)
        {
            int gid = gambler.Id;
            int kfid  = gambler.User.KfId;
            ID = kfid;
            GamblerId = gid;
            Assets = new();
            CryptoBalance = 0;
            OutstandingLoanBalance = 0;
            IsSponsored = false;
            IsWeeded = false;
            IsCracked = false; 
            IsInWithdrawal = false;
            IsLoanable = false;
            KreditScore = 100;
            Tracker = new StatTracker(gid, kfid);
            name = gambler.User.FormatUsername();
        }

        public async void Beg(UserDbModel user)
        {
            CancellationToken bToken = BegToken.Token;
            IsLoanable = true;
            var msg = await BotInstance.SendChatMessageAsync($"{user.FormatUsername()} is begging for a loan. {user.FormatUsername()} can be trused with ${KreditScore} KKK in crypto with a 1.5x return.");
            int counter = 0;
            while (!bToken.IsCancellationRequested && counter < 1000000)
            {
                await Task.Delay(TimeSpan.FromSeconds(1000000), bToken);
                counter++;
            }
            
            if (counter == 1000000)
            {
                await BotInstance.KfClient.EditMessageAsync(msg.ChatMessageId!.Value,
                    $"{user.FormatUsername()}, nobody wanted to give you a loan. !beg to continue begging for a loan.");
                await Task.Delay(TimeSpan.FromSeconds(10));
                await BotInstance.KfClient.DeleteMessageAsync(msg.ChatMessageId.Value);
            }
        }

        public void ModifyBalance(decimal amount)
        {
            CryptoBalance += amount;
        }
        public async Task<string> FormatBalanceAsync()
        {
            string str = OutstandingLoanBalance > 0 ? $"| Net Balance: {await (CryptoBalance-OutstandingLoanBalance).FormatKasinoCurrencyAsync()}":"";
            return $"Balance: {await CryptoBalance.FormatKasinoCurrencyAsync()}{str}";
        }
        public decimal[] Balance()
        {
            return new decimal[] {CryptoBalance, CryptoBalance - OutstandingLoanBalance};
        }
        public async Task SmokeCrack()
        {
            CancellationToken cToken = CrackToken.Token;
            CancellationToken wToken = WeedToken.Token;
            if (IsCracked || IsInWithdrawal)
            {
                CrackToken.Cancel();
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            LastSmokedCrack = DateTime.UtcNow;
            IsCracked = true;
            CrackTimer += TimeSpan.FromMinutes(2);
            CrackCounter++;
            HouseEdgeModifier += (decimal).05;

            for (int i = 0; i < CrackTimer.Seconds/5; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                CrackTimer -= TimeSpan.FromSeconds(5);
                if (cToken.IsCancellationRequested)
                {
                    //if you smoked more crack within that 2 minutes, add another 2 minutes of crack instead, stack the buffs and postpone the withdrawal symptoms
                    return;
                }
            }
            //now you are in withdrawal
            IsInWithdrawal = true;
            IsCracked = false;
            
            HouseEdgeModifier -= (decimal)(.06 * CrackCounter);
            for (int i = 0; i < CrackCounter*100; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                if (cToken.IsCancellationRequested)
                {
                    //if you smoke crack while in withdraw, get the basic benefits of crack back but do not reset crackcounter
                    HouseEdgeModifier = 0;
                    return;
                }
            }
            //reset the house edge modifier and crack counter after withdrawal has passed
            CrackCounter = 0;
            HouseEdgeModifier = 0;
            IsInWithdrawal = false;
        }

        public async Task SmokeWeed(TimeSpan buffLength)
        {
            FloorNugs++;
            CancellationToken wToken = WeedToken.Token;
            CancellationToken cToken = CrackToken.Token;
            if (IsWeeded)
            {
                WeedToken.Cancel();
                await Task.Delay(TimeSpan.FromSeconds(5)); 
            }
            IsWeeded = true;
            if (HouseEdgeModifier < 0)
            {
                //if you're currently in crack withdrawal
                HouseEdgeModifier /= 2;
            }
            else
            {
                HouseEdgeModifier += (decimal)0.01;
            }

            WeedTimer += buffLength;
            for (int i = 0; i < buffLength.Seconds / 5; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                WeedTimer -= TimeSpan.FromSeconds(5);
                if (wToken.IsCancellationRequested) return;
                if (cToken.IsCancellationRequested) return;
                
            }
            await Task.Delay(buffLength);
            if (HouseEdgeModifier > 0) HouseEdgeModifier -= (decimal)0.01;
            IsWeeded = false;

        }
        

        public void Withdraw(decimal amount)
        {
            CryptoBalance += amount;
        }

        public void Deposit(decimal amount)
        {
            CryptoBalance -= amount;
        }
        
        
        

        
        
        
        
        
        
        public class StatTracker
        {
            public int GamblerId;
            public int KfId;
            public decimal totalDeposited = 0;
            public decimal totalWithdrawn = 0;
            public Dictionary<WagerGame, decimal[]> totalWageredByGame; //0 is total wagered, 1 is total paid back 
            
            public StatTracker(int gid, int kfid)
            {
                GamblerId = gid;
                KfId = kfid;
                totalWageredByGame = new Dictionary<WagerGame, decimal[]>();
                foreach (var game in Enum.GetValues<WagerGame>())
                {
                    totalWageredByGame.Add(game, new decimal[] {0, 0});
                }
            }
            
            public void AddNewGameToTracker(WagerGame game)
            {
                totalWageredByGame.Add(game, new decimal[] {0, 0});
            }

            public void AddWager(WagerGame game, decimal amount, decimal net)
            {
                totalWageredByGame[game][0] += amount;
                totalWageredByGame[game][1] += net;
            }

            public void AddDeposit(decimal amount)
            {
                totalDeposited += amount;
            }
            public void AddWithdrawal(decimal amount)
            {
                totalWithdrawn += amount;
            }

            public string GetRtp()
            {
                decimal totalWagered = 0;
                decimal totalWinnings = 0;
                foreach (var wagered in totalWageredByGame.Values)
                {
                    totalWagered += wagered[0];
                    totalWinnings += wagered[1];
                }

                using var db = new ApplicationDbContext();
                var gambler = db.Gamblers.FirstOrDefaultAsync(g => g.Id == GamblerId).GetAwaiter().GetResult();
                decimal RTP = (totalWagered - totalWithdrawn - gambler!.Balance) / totalWagered;
                string returnVal = $"{gambler.User.FormatUsername()}[br]" +
                                   $"Global RTP: {RTP * 100}%[br]";
                foreach (var game in totalWageredByGame.Keys)
                {
                    returnVal += $"{game} RTP: {(totalWageredByGame[game][1] / totalWageredByGame[game][0]) * 100}%[br]";
                }

                return returnVal;
            }
        }
        
    }
    public abstract class Asset
    {
        public decimal originalValue;
        public string name;
        public AssetType type;
        public DateTime acquired;
        public List<AssetValueChangeReport> ValueChangeReports;
        public int Id;
        
        public abstract decimal GetCurrentValue();
    }

    public class AssetValueChangeReport
    {
        private decimal valueChangeAmount;
        private decimal valueChangePcnt;
        public DateTime time;
        
        public AssetValueChangeReport(decimal valueChangeAmount, decimal valueChangePcnt, DateTime time)
        {
            this.valueChangeAmount = valueChangeAmount;
            this.valueChangePcnt = valueChangePcnt;
            this.time = time;
        }

        public override string ToString()
        {
            string symbol = valueChangeAmount > 0 ? AssetValueIncreaseIndicator[true] : AssetValueIncreaseIndicator[false];
            string color = valueChangeAmount > 0 ? AssetValueIncreaseColor[true] : AssetValueIncreaseColor[false];
            string timeString = DateTime.UtcNow - time > TimeSpan.FromDays(7) ? time.ToString("g") : time.ToString("f");
            return $"{symbol}[color={color}]${valueChangeAmount} KKK({valueChangePcnt}%)[/color] {timeString}";
        }
    }

    public class Loan
    {
        public decimal amount;
        public decimal payoutAmount;
        public int payableToGambler; //gambler id
        public int payableToKf;//kfid
        public int recieverGambler;
        public int recieverKf;
        public int Id;
        public string payableTo;

        public Loan(decimal amount, int payableToGambler, int payableToKf, int recieverGambler, int recieverKf, int Id, UserDbModel payableTo)
        {
            this.amount = amount;
            payoutAmount = amount * 1.5m;
            this.payableToGambler = payableToGambler;
            this.payableToKf = payableToKf;
            this.recieverGambler = recieverGambler;
            this.recieverKf = recieverKf;
            this.Id = Id;
            this.payableTo = payableTo.FormatUsername();
        }

        public async Task<string> ToStringAsync(int kfId)
        {
            if (kfId == payableToKf) //if the person calling the command (ex !list loans) is the one who is owed
            {
                await using var db = new ApplicationDbContext();
                var gambler = await db.Gamblers.FirstOrDefaultAsync(g => g.Id == payableToGambler);
                return $"is owed ${await payoutAmount.FormatKasinoCurrencyAsync()} from {gambler!.User.FormatUsername()}";
            }
                
            return $"owes {payableTo}({payableToKf}) ${payoutAmount}KKK";
        }

        [Obsolete("Don't use base ToString, use await ToStringAsync(int kfId) instead", true)]
        public override string ToString()
        {
            return "Generated incorrect string for loan. Screenshot and send to Alogindtractor2";
        }
    }

        
        
    public class Investment : Asset //gold, silver, stake, or house
    {
        private decimal _currentValue;
        public InvestmentType investment_type;
        private DateTime _lastInterestCalculation = DateTime.UtcNow;
        public decimal[] interestRange;

        [Obsolete("Dont use base constructor", true)]
        protected Investment()
        {
            throw new Exception("Investment should not be instantiated directly. Use Gold, Silver, or House");
        }
        public Investment(int id, decimal value, decimal[] range, InvestmentType type, string name)
        {
             originalValue = value;
             _currentValue = value;
             investment_type = type;
             interestRange = range;
             Id = id;
             this.type = AssetType.Investment;
             this.name = name;
             ValueChangeReports = new();
             ValueChangeReports.Add(new AssetValueChangeReport(0, 0, DateTime.UtcNow));
        }

        public override decimal GetCurrentValue()
        {
            //apply daily interest if applicable
            if (DateTime.UtcNow - _lastInterestCalculation > TimeSpan.FromDays(1))
            {
                int interestIterations = (DateTime.UtcNow - acquired).Days;
                for (int i = 0; i < interestIterations; i++)
                {
                    
                    double range = (double)(interestRange[1] - interestRange[0]);
                    double random = _rand.NextDouble() * range;
                    random += (double)interestRange[0];
                    var oldValue = _currentValue;
                    _currentValue *= (decimal)(1 + random);
                    ValueChangeReports.Add(new AssetValueChangeReport(_currentValue - oldValue, (decimal)(random), DateTime.UtcNow - TimeSpan.FromDays(i+1)));
                }
            }
            return _currentValue;
        }
        public override string ToString()
        {
            return $"{type} {investment_type}: {name}(ID: {Id}) worth {GetCurrentValue()} {ValueChangeReports[^1]}";
        }
    }

    public class Skin : Investment
    {
        private decimal _currentValue;
        private DateTime _lastInterestCalculation = DateTime.UtcNow;
        private string _objName;
        private string _tag;
        private string _color;
        private string _emoji;

        public Skin(int id, decimal value, string obj, string tag, string color, string emoji) : base(id, value, CsSkinAprRange, InvestmentType.Skin, $"[color={color}][b][{emoji}{tag}]{obj}[/b][/color]")
        {
            _objName = obj;
            _tag = tag;
            _color = color;
            _emoji = emoji;
        }
        
        //use same getCurrentValue as investment
        
        public override string ToString()
        {
            return $"{name}(ID: {Id}) {ValueChangeReports[^1]}";
        }
    }

    public class Smashable : Asset // computer equipment
    {
        
        public new AssetType type = AssetType.Smashable;

        public Smashable()
        {
            
        }

        public override decimal GetCurrentValue()
        {
            return originalValue;
        }

        public override string ToString()
        {
            
        }
    }

    
    public class Car : Asset
    {
        private decimal _currentValue;
        public new AssetType type = AssetType.Car;
        public Cars car_type;
        public decimal job_value;
        
        [Obsolete("Dont use base constructor", true)]
        public Car()
        {
            throw new Exception("Car should not be instantiated directly. Use Car(int id, string name, decimal value, Cars car_type, decimal job_value)");
        }
        public Car(Cars type)
        {
            acquired = DateTime.UtcNow;
            car_type = type;
            _currentValue = CarPrices[type];
            originalValue = _currentValue;
            job_value = CarPrices[type] / 10;
            name = type.ToString();
            ValueChangeReports = new();
        }

        public override decimal GetCurrentValue()
        {
            return _currentValue;
        }

        public async Task SetId(GamblerDbModel gambler) //sets the id of the car to a unique number when you buy it so you can interact with it later 
        {
            int id = Money.GetRandomNumber(gambler, 0, 999999999);
            int counter = 0;
            for (int i = 0;
                 i < BotInstance.BotServices.KasinoShop!.Gambler_Profiles[gambler.User.KfId].Assets.Count;
                 i++)
            {
                if (BotInstance.BotServices.KasinoShop!.Gambler_Profiles[gambler.User.KfId].Assets.ElementAt(i).Value.Id == id)
                {
                    id = Money.GetRandomNumber(gambler, 0, 999999999);
                    i = -1;
                    counter++;
                    if (counter > 10000)
                    {
                        throw new Exception("failed to generate unique ID for car after 10000 attempts");
                    }
                }
            }
        }

        public async Task ProcessWorkJob(GamblerDbModel gambler)
        {
            decimal oldVal = _currentValue;
            _currentValue -= job_value / 5;
            ValueChangeReports.Add(new AssetValueChangeReport(-job_value / 5, (_currentValue - oldVal) / oldVal, DateTime.UtcNow));
            BotInstance.BotServices.KasinoShop!.Gambler_Profiles[gambler.User.KfId].ModifyBalance(job_value);
            if (_currentValue <= 0) // if your car dies it gets removed from your asset list
            {
                BotInstance.BotServices.KasinoShop!.Gambler_Profiles[gambler.User.KfId].Assets.Remove(Id);
                await BotInstance.SendChatMessageAsync(
                    $"{gambler.User.FormatUsername()} totalled their car on the way home from work.", true,
                    autoDeleteAfter: TimeSpan.FromSeconds(10));
            }
            
        }

        public override string ToString()
        {
            return $"{type} {name} worth ${GetCurrentValue()} KKK";
        }
        
    }
    
    
    //KasinoShop.[]Market - used to generate a list of items for you to buy when you interact with the shop. takes your shop profiles current state into account
    //for example if you have a car, you can take meth queen home with you and buy meth
    //markets get new options every day except drug market which stays the same outside of prices
    
    
    
    
    public class SkinMarket
    {
        private List<Skin> _skins;
        private DateTime _opened = DateTime.UtcNow;

        public SkinMarket(List<Skin> skins)
        {
            _skins = skins;
        }
        public bool Old()
        {
            return DateTime.UtcNow - _opened > TimeSpan.FromDays(1);
        }
    }
    

    public static readonly decimal CrackPrice = 10000m;
    public static readonly decimal WeedPricePerHour = 1000m;
    public static readonly TimeSpan WeedNugLength = TimeSpan.FromMinutes(6);
    
    public static readonly decimal[] ShoeAprRange = { -0.05m, 0.05m };
    public static readonly decimal[] CsSkinAprRange = { -0.25m, 0.25m };
    public static decimal HomeApr = 0.1m;
    
    
    
    public static readonly Dictionary<Cars, Car> DefaultCars = new()
    {
        {Cars.Civic, new Car(Cars.Civic)},
        {Cars.Audi, new Car(Cars.Audi)},
        {Cars.Bentley, new Car(Cars.Bentley)},
        {Cars.Bmw, new Car(Cars.Bmw)}
    };
    public static readonly Dictionary<Cars, decimal> CarPrices = new()
    {
        { Cars.Civic , 2_000_000 },
        { Cars.Audi, 4_000_000 },
        { Cars.Bentley , 6_000_000 },
        { Cars.Bmw , 8_000_000 },
    };

    public static readonly List<string> CsSkinObjects = new()
    {
        "P2000",
        "USP-S",
        "Glock-18",
        "Dual Berettas",
        "P250",
        "Tec-9",
        "Five-SeveN",
        "CZ75-Auto",
        "R8 Revolver",
        "Desert Eagle",
        "Mac-10",
        "UMP-45",
        "MP5 SD",
        "MP7",
        "MP9",
        "P90",
        "PP-Bizon",
        "Nova",
        "Sawed-Off",
        "Mag-7",
        "XM1014",
        "SSG 08",
        "Galil AR",
        "FAMAS",
        "AK-47",
        "M4A1-S",
        "SG 553",
        "M4A4",
        "AUG",
        "AWP",
        "G3SG1",
        "SCAR-20",
        "Bayonet",
        "Bowie Knife",
        "Butterfly Knife",
        "Falchion Knife",
        "Flip Knife",
        "Gut Knife",
        "Huntsman Knife",
        "Karambit",
        "M9 Bayonet",
        "Navaja Knife",
        "Nomad Knife",
        "Paracord Knife",
        "Shadow Daggers",
        "Skeleton Knife",
        "Stiletto Knife",
        "Survival Knife",
        "Talon Knife",
        "Ursus Knife",
        "Kukri Knife",
        "Sport Gloves",
        "Specialist Gloves",
        "Driver Gloves",
        "Hand Wraps",
        "Moto Gloves",
        "Bloodhound Gloves",
        "Hydra Gloves",
        "Broken Fang Gloves"
    };

    public static readonly Dictionary<string, decimal> CsSkinTags = new()
    {
        {"SNEED", 0},
        {"R", 0},
        {"RIGGED", 0},
        {"GREEDY", 0},
        {"DEWISH", 0},
        {"JEWISH", 0},
        {"SCAMMER", 0},
        {"SCAM", 0},
        {"5", 0},
        {"9", 0},
        {"OSRS", 0},
        {"666", 0},
        {"CRACK", 0},
        {"WEED", 0},
        {"EEEEEEEEEE", 0},
        {"COFFEE", 0},
        {"FATGO", 0},
        {"YO", 0},
        {"ELF", 0},
        {"OMFG", 0},
        {"IHML", 0},
        {"FUCKIN DEWD", 0},
        {"MILF", 0},
        {"KKK", 0},
        {"KASINO", 0},
        {"METH", 0},
        {"GOOBR", 0},
        {"TRAPPER", 0},
        {"TRAPPERTURD", 0},
        {"CHRISTMAS", 0},
        {"MR.CHRISTMAS", 0},
        {"DEPAKOTE", 0},
        {"RAT", 0},
        {"NULL", 0},
        {"MATI", 0},
        {"GERMAN RAP", 0},
        {"EVIL EDDIE", 0},
        {"NASTY NOAH", 0},
        {"BOSSMAN", 0},
        {"RATDAD", 0},
        {"PICKLETIME", -1000000m},
        
    };


    public static string GetRandomColor(GamblerDbModel gambler)
    {
        int r = Money.GetRandomNumber(gambler, 0, 255);
        int g = Money.GetRandomNumber(gambler, 0, 255);
        int b = Money.GetRandomNumber(gambler, 0, 255);
        return $"{r:X2}{g:X2}{b:X2}";
    }

    public static readonly Dictionary<string, decimal> CsSkinEmotes = new()
    {
        {"🤣", 0},
        {"😂", 0},
        {"🙂", 0},
        {"😝", 0},
        {"🤨", 0},
        {"🙄", 0},
        {"🤥", 0},
        {"🤧", 0},
        {"🤯", 0},
        {"😭", 0},
        {"😤", 0},
        {"💩", 0},
        {"💢", 0},
        {"💥", 0},
        {"💤", 0},
        {"🫵", 0},
        {"🙏", 0},
        {"🎅", 0},
        {"🐀", 0},
        {"🐹", 0},
        {"🦨", 0},
        {"🐦‍🔥", 0},
        {"🐍", 0},
        {"🐉", 0},
        {"🦞", 0},
        {"☘", 0},
        {"🍀", 0},
        {"🥩", 0},
        {"🚔", 0},
        {"🚗", 0},
        {"✈", 0},
        {"☁", 0},
        {"🌨", 0},
        {":winner:", 0},
        {":juice:", 0},
        {":ross:", 0},
        {"♠", 0},
        {"♥", 0},
        {"♦", 0},
        {"♣", 0},
        {"💎", 0},
        {"🪫", 0},
        {"🪙", 0},
        {"💵", 0},
        {"🧲", 0},
        {"💊", 0},
        {"🚬", 0},
        {"🪪", 0},
        {"🚭", 0},
        {"✡", 0},
        {"❓", 0},
        {"💲", 0},
        {"🆗", 0},
        {"🤡", 0},
        {"👅", 0},
        {"👴", 0},
        {"🛹", 0},
        {"🥒", -1000000m},
        {"🎄", 0},
        {"🕹", 0},
        {"🎰", 0},
        
    };

    

    public static readonly Dictionary<bool, string> AssetValueIncreaseIndicator = new() { {false, "🔻"}, {true, "🔺"} };
    public static readonly Dictionary<bool, string> AssetValueIncreaseColor = new() { {false, "red"}, {true, "lightgreen"} };
    
    public static Dictionary<ShoeBrand, decimal> ShoePrices(GamblerDbModel gambler)
    {
        
        return new Dictionary<ShoeBrand, decimal>
        {
            { ShoeBrand.Yeezy , Money.GetRandomNumber(gambler, 6_000, 100_000) }, 
            { ShoeBrand.Adidas , Money.GetRandomNumber(gambler, 1_800, 50_000) },
            { ShoeBrand.Jordan , Money.GetRandomNumber(gambler, 9_000, 380_000) },
        };
    }

    private static List<string> SmashCarousel = new()
    {
        "https://i.ddos.lgbt/u/KhMr9v.webp",
        "https://i.ddos.lgbt/u/KYaOqH.webp",
        "https://i.ddos.lgbt/u/w3wAyB.webp",
        "https://i.ddos.lgbt/u/c4znnv.webp",
        "https://i.ddos.lgbt/u/qGHbNp.webp",
        "https://i.ddos.lgbt/u/65lz4m.webp",
        "https://i.ddos.lgbt/u/ZCDWeO.webp",
        "https://i.ddos.lgbt/u/2025-12-12_19:17:16.gif",
        "https://i.ddos.lgbt/u/oBXBV4.webp",
        "https://i.ddos.lgbt/u/2025-12-12_19:08:15.gif",
        "https://i.ddos.lgbt/u/fuxIHW.webp",
        "https://i.ddos.lgbt/u/0dtwl3.webp",
        
    };

    public static readonly decimal GoldBasePriceOz = 300000;
    public static readonly decimal[] GoldInterestRange = new decimal[] { 0.01m, 0.05m };
    public static readonly decimal SilverBasePriceOz = 10000;
    public static readonly decimal[] SilverInterestRange = new decimal[] { 0.01m, 0.05m };
    public static readonly decimal BaseHousePrice = 100000000;
    public static readonly decimal[] HouseInterestRange = new decimal[] { 0.01m, 0.15m };
    public static readonly decimal[] CryptoStakeInterestRange = new decimal[] { -0.01m, 0.05m };
    
    public static String GetRandomSmashImage(GamblerDbModel gambler)
    {
        int rand = Money.GetRandomNumber(gambler, 0, SmashCarousel.Count - 1);
        return SmashCarousel[rand];
    }
        
    
}





public enum SmashableType
{
    Headphones,
    Keyboard,
    Mouse
}
public enum InvestmentType
{
    Shoes,
    Stake,
    Gold,
    Silver,
    Skin,
    House,
    Random
}

public enum AssetType
{
    Investment,
    Smashable,
    Car,
    Random
}

public enum ShoeBrand
{
    Yeezy,
    Adidas,
    Jordan,
    Nike
}

public enum Cars
{
    Civic,
    Bentley,
    Audi,
    Bmw
}

public enum LiabilityType
{
    Loan,
    Sponsorship
}

