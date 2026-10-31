namespace CodexTray.Core;

public static class ApiCreditWarnings
{
    public static int Band(decimal remainingPercent) => remainingPercent switch
    {
        <= 0 => 4, <= 5 => 3, <= 10 => 2, <= 25 => 1, _ => 0
    };
}
