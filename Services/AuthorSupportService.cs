using FileExplorer.Models;
using FileExplorer.Support;

namespace FileExplorer.Services;

public static class AuthorSupportService
{
    private static readonly AuthorSupportInfo Cached = new()
    {
        UsdtBep20Address = AuthorSupportConstants.UsdtBep20Address,
        AuthorName = AuthorSupportConstants.AuthorName,
        NetworkLabel = AuthorSupportConstants.NetworkLabel
    };

    public static AuthorSupportInfo GetInfo() => Cached;

    public static bool HasWalletAddress() => true;

    public static string GetBscScanAddressUrl(string address) =>
        $"https://bscscan.com/address/{address.Trim()}";
}
