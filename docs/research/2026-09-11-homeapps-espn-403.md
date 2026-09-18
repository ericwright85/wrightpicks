# HomeApps ESPN scoreboard 403 investigation

Date: 2026-09-11, approximately 15:52–15:55 UTC.

Scope: User-requested investigation of the `ewrpi/HomeApps` repository, now mapped locally at `C:/Main/code/wright-picks`. This note was moved here from the Atlas workspace at the user's request. The initial investigation changed no application source; the one-line implementation is recorded below. Nothing was deployed.

## Confirmed facts

Source inspected: https://github.com/ewrpi/HomeApps/blob/master/HomeAppsLib/LibCommon.cs (retrieved from raw.githubusercontent.com, master at investigation time).

`UpdateNFLMatchups(bool)` starts a thread running `DoUpdateNFLMatchupsESPN()`. The method creates a default `System.Net.WebClient`, downloads the plain HTTP ESPN NFL scoreboard URL, deserializes `API.ESPN.Feed`, loads unfinished NFL matchups, matches teams, applies scores/status through `UpdateMatchup`, conditionally submits database changes, and checks whether weeks should close. The exception handler sends `ex.ToString()` by email; it does not capture the HTTP error response body. The isolated reproduction invoked only the download, never the full method, database writes, or email.

HomeAppsLib.csproj declares .NET Framework v3.5. Tests used both .NET 10.0.11 WebClient and Windows PowerShell's .NET Framework CLR 4.0.30319.42000; the deployed application runtime was not inspected.

The exact endpoint was `http://site.api.espn.com/apis/site/v2/sports/football/nfl/scoreboard`, also tested with HTTPS. Both protocols produced the same results:

| WebClient User-Agent | Result |
| --- | --- |
| Not set (original code) | 403 Forbidden |
| Mozilla/5.0 | 403 Forbidden |
| Full Chrome 140 Windows User-Agent | 403 Forbidden |
| HomeApps/1.0 or HomeApps | 403 Forbidden |
| PostmanRuntime/7.43.0 | 403 Forbidden |
| curl/8.21.0 | Success; valid JSON, season 2026, week 1, 16 events |

`curl/8.0.0` also succeeded over HTTPS. The failing responses identify `Server: AkamaiGHost`, contain an HTML Access Denied page and an edgesuite.net reference, and do not redirect. One captured reference was `18.92cadc17.1789141928.30972351`. The HTML names an HTTP URL even when the actual transport was HTTPS; this is not evidence of a redirect.

With .NET Framework, the sequence no User-Agent → curl/8.21.0 → no User-Agent gave failure → success → failure independently for both HTTP and HTTPS. No Accept, Referer, authentication, cookies, or query parameters were necessary for success. Individually adding Accept, Accept-Encoding, or Referer to the failing HTTPS request did not fix it.

An initial curl test of query parameters succeeded because it retained curl's default User-Agent. Subsequent WebClient tests with the same query parameters and no User-Agent failed. Query parameters are not the demonstrated fix.

## Conclusion and minimal correction

The reproduced failure depends on the User-Agent value and is returned by ESPN's Akamai edge. Merely changing HTTP to HTTPS, or supplying an arbitrary/browser User-Agent, is insufficient in the tested environment.

The smallest verified workaround is one line after constructing WebClient, before DownloadString:

```csharp
client.Headers[System.Net.HttpRequestHeader.UserAgent] = "curl/8.21.0";
```

It succeeds with the original HTTP URL. HTTPS is independently desirable for transport protection but is not the cause/fix demonstrated here. No TLS-global-setting change is justified by the observed 403. This is a compatibility workaround based on current edge behavior, not an ESPN guarantee that this User-Agent will remain accepted.

## Implementation follow-up — 2026-09-18

At the user's request, added exactly the User-Agent assignment above to `C:/Main/code/wright-picks/HomeAppsLib/LibCommon.cs`, in `DoUpdateNFLMatchupsESPN`, immediately before DownloadString (line 594). Kept the existing URL and TLS setting unchanged. The target repository has unrelated existing changes; none were modified. The source diff contains one added line and passes `git diff --check`.

Re-ran the isolated WebClient request with the added header: successful JSON response, season 2026, week 2, 16 events. Did not execute the full method, send email, write application data, build, or deploy. Production-server verification remains open.

## Inferences

- The response and controlled tests strongly indicate Akamai request filtering keyed to User-Agent/client classification. ESPN's private edge rule and its rationale cannot be identified from this response alone.
- Browser success may reflect its complete request fingerprint, a different edge/network path, or another browser-specific difference. A generic Chrome User-Agent alone did not reproduce browser success. The user's browser request was not captured, so no exact explanation is claimed.

## Open questions and verification limits

- Does the deployed server show the same failure/success pairing? Its outbound path and runtime were not available for verification.
- What exact Akamai policy produced the denial? This requires ESPN/Akamai-side logs or configuration, using a captured denial reference.
- The full application update and JSON model deserialization were not executed. Successful response JSON was parsed and its season/week/event count inspected.

## Read-only reproduction on the affected server

Run in Windows PowerShell. This performs only two public GET requests and does not call HomeApps application logic:

```powershell
$url = 'http://site.api.espn.com/apis/site/v2/sports/football/nfl/scoreboard'
foreach ($ua in @('', 'curl/8.21.0')) {
    $client = New-Object System.Net.WebClient
    if ($ua) { $client.Headers['User-Agent'] = $ua }
    try {
        $feed = $client.DownloadString($url) | ConvertFrom-Json
        "SUCCESS UA='$ua' week=$($feed.week.number) events=$($feed.events.Count)"
    } catch {
        $err = $_.Exception
        while ($err.InnerException) { $err = $err.InnerException }
        "FAIL UA='$ua' $($err.Message)"
        if ($err.Response) { $err.Response.Headers.ToString() }
    } finally { $client.Dispose() }
}
```
