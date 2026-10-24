#requires -Version 7.4
<#
.SYNOPSIS
Tests Cat Quest's own ChatGPT public-client registration before any product UI integration.
.DESCRIPTION
Run with -Run to open the system browser. Only a synthetic task is sent. Each attempt
uses an isolated host/client registration; no Codex, Google or player credentials are read.
Tokens stay in memory except for an encrypted DPAPI round-trip file removed in finally.
The issued renewable session is revoked on exit. Reports never contain tokens or callbacks.
Run with -SelfTest for offline checks; this never opens a browser or contacts OpenAI.
#>
[CmdletBinding(DefaultParameterSetName = 'Help')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Live')][switch]$Run,
    [Parameter(Mandatory, ParameterSetName = 'Tests')][switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Issuer = 'https://auth.openai.com'
$script:Resource = 'https://api.openai.com/v1'
$script:Steps = [Collections.Generic.List[string]]::new()

function ConvertTo-Base64Url([byte[]]$Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}
function ConvertFrom-Base64Url([string]$Value) {
    if ($Value -notmatch '^[A-Za-z0-9_-]+$') { throw 'Invalid base64url.' }
    $padded = $Value.Replace('-', '+').Replace('_', '/')
    switch ($padded.Length % 4) { 2 { $padded += '==' } 3 { $padded += '=' } 1 { throw 'Invalid base64url length.' } }
    return ,([Convert]::FromBase64String($padded))
}
function New-RandomToken { ConvertTo-Base64Url ([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) }
function Get-Challenge([string]$Verifier) {
    ConvertTo-Base64Url ([Security.Cryptography.SHA256]::HashData([Text.Encoding]::ASCII.GetBytes($Verifier)))
}
function Get-Json([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { throw 'Empty JSON.' }
    # JsonDocument catches trailing text and malformed JSON before PowerShell conversion.
    $document = $null
    try {
        $document = [Text.Json.JsonDocument]::Parse($Value)
        return ConvertFrom-Json -InputObject $Value -AsHashtable -Depth 32
    } catch { throw 'Invalid JSON.' } # Parser diagnostics must never echo token payloads.
    finally { if ($document) { $document.Dispose() } }
}
function Read-Callback([string]$Target, [string]$ExpectedState) {
    if ($Target.Length -gt 8192 -or $Target -notmatch '^/auth/callback\?(.+)$') { throw 'Invalid callback path.' }
    $values = @{}
    foreach ($item in $Matches[1].Split('&')) {
        $pair = $item.Split('=', 2)
        if ($pair.Count -ne 2) { throw 'Invalid callback parameter.' }
        $key = [Uri]::UnescapeDataString($pair[0])
        $value = [Uri]::UnescapeDataString($pair[1].Replace('+', ' '))
        if ($values.ContainsKey($key)) { throw 'Duplicate callback parameter.' }
        $values[$key] = $value
    }
    if (-not $values.ContainsKey('state') -or $values.state -cne $ExpectedState) { throw 'Invalid callback state.' }
    if ($values.ContainsKey('error')) { throw 'Authorization was declined or cancelled.' }
    if (-not $values.ContainsKey('code') -or [string]::IsNullOrWhiteSpace($values.code) -or $values.code.Length -gt 4096) {
        throw 'Missing authorization code.'
    }
    if (-not $values.ContainsKey('client_id') -or $values.client_id -cnotmatch '^oaiapp_[A-Za-z0-9_-]+$') {
        throw 'Registration did not issue an application client ID.'
    }
    return $values
}
function Test-IdToken([string]$Token, [hashtable]$Jwks, [string]$ClientId, [string]$Nonce, [long]$Now) {
    if ($Token.Length -gt 32768) { throw 'ID token is too large.' }
    $parts = $Token.Split('.')
    if ($parts.Count -ne 3) { throw 'Invalid ID token.' }
    $header = Get-Json ([Text.Encoding]::UTF8.GetString((ConvertFrom-Base64Url $parts[0])))
    $claims = Get-Json ([Text.Encoding]::UTF8.GetString((ConvertFrom-Base64Url $parts[1])))
    if ($header.alg -cne 'RS256' -or -not $header.ContainsKey('kid')) { throw 'Unsupported ID token signature.' }
    $keys = @($Jwks.keys | Where-Object { $_.kid -ceq $header.kid -and $_.kty -ceq 'RSA' })
    if ($keys.Count -ne 1) { throw 'Unknown ID token signing key.' }
    $parameters = [Security.Cryptography.RSAParameters]::new()
    $parameters.Modulus = ConvertFrom-Base64Url $keys[0].n
    $parameters.Exponent = ConvertFrom-Base64Url $keys[0].e
    $rsa = [Security.Cryptography.RSA]::Create()
    try {
        $rsa.ImportParameters($parameters)
        $valid = $rsa.VerifyData([Text.Encoding]::ASCII.GetBytes($parts[0] + '.' + $parts[1]),
            (ConvertFrom-Base64Url $parts[2]), [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        if (-not $valid) { throw 'Invalid ID token signature.' }
    } finally { $rsa.Dispose() }
    if ($claims.iss -cne $script:Issuer -or -not (@($claims.aud) -ccontains $ClientId) -or
        $claims.nonce -cne $Nonce -or [string]::IsNullOrWhiteSpace($claims.sub) -or [long]$claims.exp -le $Now) {
        throw 'ID token identity, nonce, audience or expiry validation failed.'
    }
    if ($claims.ContainsKey('nbf') -and [long]$claims.nbf -gt ($Now + 60)) { throw 'ID token is not valid yet.' }
    if ($claims.ContainsKey('azp') -and $claims.azp -cne $ClientId) { throw 'ID token authorized party mismatch.' }
    if (@($claims.aud).Count -gt 1 -and -not $claims.ContainsKey('azp')) { throw 'ID token requires an authorized party.' }
    return $claims
}
function Test-PlanPermission([hashtable]$Tokens) {
    if (-not $Tokens.ContainsKey('scope') -or
        -not ($Tokens.scope.Split(' ', [StringSplitOptions]::RemoveEmptyEntries) -ccontains 'chatgpt.tokens.use.direct')) {
        throw 'ChatGPT plan usage was not granted. No inference request was sent.'
    }
    if ($Tokens.token_type -ine 'Bearer' -or [string]::IsNullOrWhiteSpace($Tokens.access_token) -or
        [string]::IsNullOrWhiteSpace($Tokens.refresh_token)) { throw 'Incomplete OAuth credentials.' }
}
function New-Request([string]$Url, [string]$Method = 'GET', [string]$Bearer = '') {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), $Url)
    if ($Bearer) { $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Bearer) }
    return $request
}
function Assert-Http([Net.Http.HttpResponseMessage]$Response, [string]$Stage) {
    if (-not $Response.IsSuccessStatusCode) {
        # Do not print the response body: auth servers may echo codes or credentials.
        throw ($Stage + ' failed (HTTP ' + [int]$Response.StatusCode + ').')
    }
}
function Send-Json([Net.Http.HttpClient]$Http, [Net.Http.HttpRequestMessage]$Request, [string]$Stage) {
    $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(20))
    $response = $null
    try {
        $response = $Http.SendAsync($Request, $deadline.Token).GetAwaiter().GetResult()
        Assert-Http $response $Stage
        $body = $response.Content.ReadAsStringAsync($deadline.Token).GetAwaiter().GetResult()
        return Get-Json $body
    } catch {
        if ($_.Exception.Message -like ($Stage + ' failed (HTTP *')) { throw $_.Exception.Message }
        throw ($Stage + ' failed or timed out; no credentials were logged.')
    } finally { if ($response) { $response.Dispose() }; $Request.Dispose(); $deadline.Dispose() }
}
function New-FormRequest([string]$Url, [hashtable]$Fields) {
    $request = New-Request $Url 'POST'
    $pairs = [Collections.Generic.Dictionary[string,string]]::new()
    foreach ($key in $Fields.Keys) { $pairs.Add($key, [string]$Fields[$key]) }
    $request.Content = [Net.Http.FormUrlEncodedContent]::new($pairs)
    return $request
}
function New-StreamState { return @{ text = ''; completed = $false; size = 0 } }
function Read-StreamEvent([hashtable]$State, [string]$Data) {
    if ($Data -eq '[DONE]') { return }
    $event = Get-Json $Data
    if ($State.completed) { throw 'Unexpected event after response completion.' }
    switch -CaseSensitive ($event.type) {
        'response.output_text.delta' {
            $State.text += [string]$event.delta
            if ($State.text.Length -gt 8192) { throw 'Inference output exceeded the verification limit.' }
        }
        'response.completed' {
            if (-not $event.ContainsKey('response') -or $event.response.status -cne 'completed') {
                throw 'Invalid response completion event.'
            }
            $State.completed = $true
        }
        { $_ -cin @('response.failed', 'response.incomplete', 'error') } { throw 'Inference failed or was incomplete.' }
    }
}
function Get-ChatReply([hashtable]$State) {
    if (-not $State.completed) { throw 'Stream ended without response.completed.' }
    $reply = Get-Json $State.text
    if ($reply -isnot [Collections.IDictionary] -or $reply.Count -ne 2 -or
        -not $reply.ContainsKey('message') -or -not $reply.ContainsKey('drafts') -or
        $reply.message -isnot [string] -or [string]::IsNullOrWhiteSpace($reply.message) -or
        $reply.message.Length -gt 240 -or $reply.drafts -isnot [array] -or $reply.drafts.Count -ne 0) {
        throw 'Inference did not return a valid synthetic chat reply.'
    }
    return $reply
}
function Read-ChatStream([IO.StreamReader]$Reader, [Threading.CancellationToken]$Cancellation) {
    $streamState = New-StreamState; $dataLines = [Collections.Generic.List[string]]::new()
    while (-not $streamState.completed) {
        $line = $Reader.ReadLineAsync($Cancellation).AsTask().GetAwaiter().GetResult()
        if ($null -eq $line) { break }
        $streamState.size += $line.Length
        if ($streamState.size -gt 262144) { throw 'Event stream exceeded the verification limit.' }
        if ($line.Length -eq 0) {
            if ($dataLines.Count) { Read-StreamEvent $streamState ($dataLines -join "`n"); $dataLines.Clear() }
        } elseif ($line.StartsWith('data:')) { $dataLines.Add($line.Substring(5).TrimStart(' ')) }
    }
    return Get-ChatReply $streamState
}
function Assert-StreamContent([Net.Http.HttpResponseMessage]$Response) {
    # Some intermediaries omit Content-Type. A missing header is not proof of a
    # malformed stream: the SSE parser and required terminal event validate it.
    $contentType = $Response.Content.Headers.ContentType
    if ($null -ne $contentType -and $contentType.MediaType -ine 'text/event-stream') {
        throw 'Inference did not return an event stream.'
    }
}
function Receive-Callback([Net.Sockets.TcpListener]$Listener, [string]$State) {
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(2)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (-not $Listener.Pending()) { Start-Sleep -Milliseconds 50; continue }
        $client = $Listener.AcceptTcpClient()
        try {
            $client.ReceiveTimeout = 2000; $client.SendTimeout = 2000
            $stream = $client.GetStream(); $buffer = [byte[]]::new(16384); $count = 0; $request = ''
            while ($count -lt $buffer.Length -and -not $request.Contains("`r`n`r`n")) {
                $read = $stream.Read($buffer, $count, $buffer.Length - $count)
                if ($read -eq 0) { break }
                $count += $read; $request = [Text.Encoding]::ASCII.GetString($buffer, 0, $count)
            }
            $first = $request.Split("`r`n")[0].Split(' ')
            $callback = $null; $rejection = ''
            if ($first.Count -eq 3 -and $first[0] -ceq 'GET' -and $request.Contains("`r`n`r`n")) {
                try { $callback = Read-Callback $first[1] $State } catch { $rejection = $_.Exception.Message }
            }
            $valid = $null -ne $callback
            $body = if ($valid) { 'Authorization received. Return to Cat Quest; validation is still running.' } else { 'Authorization callback was not accepted. Return to Cat Quest.' }
            $status = if ($valid) { '200 OK' } else { '400 Bad Request' }
            $reply = [Text.Encoding]::UTF8.GetBytes("HTTP/1.1 $status`r`nContent-Type: text/plain; charset=utf-8`r`nCache-Control: no-store`r`nConnection: close`r`nContent-Length: $([Text.Encoding]::UTF8.GetByteCount($body))`r`n`r`n$body")
            $stream.Write($reply, 0, $reply.Length)
            if ($valid) { return $callback }
            if ($rejection -eq 'Authorization was declined or cancelled.') { throw $rejection }
            # Ignore favicon, wrong state and malformed local requests; await the actual callback.
        } catch [IO.IOException] { } finally { $client.Dispose() }
    }
    throw 'Browser authorization timed out after two minutes; eligibility remains unverified.'
}
function Test-ProtectedStorage([string]$Path, [byte[]]$Plaintext) {
    $encrypted = [Security.Cryptography.ProtectedData]::Protect($Plaintext, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    $temporary = $Path + '.tmp'
    try {
        [IO.File]::WriteAllBytes($temporary, $encrypted)
        [IO.File]::Move($temporary, $Path, $true)
        $restored = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($Path), $null,
            [Security.Cryptography.DataProtectionScope]::CurrentUser)
        try {
            if ([Convert]::ToBase64String($restored) -cne [Convert]::ToBase64String($Plaintext)) { throw 'Protected credential round-trip failed.' }
        } finally { [Array]::Clear($restored, 0, $restored.Length) }
    } finally {
        [Array]::Clear($encrypted, 0, $encrypted.Length)
        foreach ($file in @($Path, $temporary)) { if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) } }
    }
}

function Invoke-LiveVerification([string]$Directory) {
    $handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false
    $http = [Net.Http.HttpClient]::new($handler); $http.Timeout = [Threading.Timeout]::InfiniteTimeSpan
    $listener = $null; $tokens = $null; $callback = $null; $discovery = $null
    try {
        $discovery = Send-Json $http (New-Request ($script:Issuer + '/.well-known/openid-configuration')) 'Discovery'
        foreach ($field in @('authorization_endpoint', 'token_endpoint', 'jwks_uri', 'revocation_endpoint')) {
            $uri = [Uri]$discovery[$field]
            if ($uri.Scheme -cne 'https' -or $uri.Host -cne 'auth.openai.com' -or -not $uri.IsDefaultPort -or $uri.UserInfo) {
                throw 'Discovery returned an unexpected endpoint.'
            }
        }
        if ($discovery.issuer -cne $script:Issuer) { throw 'Discovery issuer mismatch.' }
        $script:Steps.Add('Official OpenID discovery validated')
        $state = New-RandomToken; $nonce = New-RandomToken; $verifier = New-RandomToken
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $listener.Start(4)
        $redirect = 'http://127.0.0.1:' + $listener.LocalEndpoint.Port + '/auth/callback'
        $parameters = [ordered]@{
            client_id = 'dynamic_agent_client'; agent_name_hint = 'Cat Quest OAuth Verification'
            ext_agent_host_id = ('urn:uuid:' + [Guid]::NewGuid().ToString())
            response_type = 'code'; redirect_uri = $redirect
            scope = 'openid profile email offline_access resource.invoke chatgpt.tokens.use.direct'
            resource = $script:Resource; state = $state; nonce = $nonce
            code_challenge_method = 'S256'; code_challenge = (Get-Challenge $verifier)
        }
        $query = ($parameters.GetEnumerator() | ForEach-Object { [Uri]::EscapeDataString($_.Key) + '=' + [Uri]::EscapeDataString([string]$_.Value) }) -join '&'
        Write-Host 'Opening the browser for Cat Quest OAuth Verification. Complete sign-in/consent there (two-minute timeout).'
        Write-Host ('Report directory: ' + $Directory)
        Start-Process -FilePath ($discovery.authorization_endpoint + '?' + $query)
        $callback = Receive-Callback $listener $state
        $listener.Stop(); $listener = $null
        $script:Steps.Add('Own application registration callback and state validated')
        $tokens = Send-Json $http (New-FormRequest $discovery.token_endpoint @{
            grant_type = 'authorization_code'; client_id = $callback.client_id; code = $callback.code
            code_verifier = $verifier; redirect_uri = $redirect; resource = $script:Resource
        }) 'Code exchange'
        $jwks = Send-Json $http (New-Request $discovery.jwks_uri) 'Signing keys'
        $null = Test-IdToken $tokens.id_token $jwks $callback.client_id $nonce ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())
        Test-PlanPermission $tokens
        $script:Steps.Add('ID token signature, identity, nonce and plan permission validated')
        $bytes = [Text.Encoding]::UTF8.GetBytes(($tokens | ConvertTo-Json -Compress -Depth 8))
        try { Test-ProtectedStorage (Join-Path $Directory 'credential-roundtrip.dpapi') $bytes }
        finally { [Array]::Clear($bytes, 0, $bytes.Length) }
        $script:Steps.Add('Windows current-user DPAPI and atomic file round-trip passed; file removed')
        # Exercise one refresh; always replace a rotated refresh token before further checks.
        $renewed = Send-Json $http (New-FormRequest $discovery.token_endpoint @{
            grant_type = 'refresh_token'; client_id = $callback.client_id; refresh_token = $tokens.refresh_token; resource = $script:Resource
        }) 'Token refresh'
        foreach ($key in $renewed.Keys) { $tokens[$key] = $renewed[$key] }
        Test-PlanPermission $tokens
        $script:Steps.Add('Token refresh completed')
        $catalog = Send-Json $http (New-Request ($script:Resource + '/models') 'GET' $tokens.access_token) 'Model discovery'
        $models = @($catalog.models | Where-Object { $_.visibility -ceq 'list' -and -not [string]::IsNullOrWhiteSpace($_.slug) })
        if ($models.Count -eq 0) { throw 'The account returned no selectable models.' }
        $script:Steps.Add('Account-specific model catalog retrieved')
        # Model slugs are public identifiers, not account identity or credentials.
        if ($models[0].slug -cmatch '^[A-Za-z0-9_.:/-]{1,100}$') { $script:Steps.Add('Selected model: ' + $models[0].slug) }
        $request = New-Request ($script:Resource + '/responses') 'POST' $tokens.access_token
        $body = @{
            model = $models[0].slug; store = $false; stream = $true
            instructions = 'Return only JSON with exactly message (a short greeting, at most 240 characters) and drafts (an empty array). Do not query tasks or propose changes.'
            input = @(@{ role = 'user'; content = '{"title":"Organize three lecture notes into a short outline","description":"Group related ideas and write a concise outline.","subtasks":[]}' })
        } | ConvertTo-Json -Compress -Depth 8
        $request.Content = [Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')
        $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(20)); $response = $null; $reader = $null
        $stage = 'request'
        try {
            $response = $http.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $deadline.Token).GetAwaiter().GetResult()
            Assert-Http $response 'Inference'
            Assert-StreamContent $response
            $stage = 'stream'
            $reader = [IO.StreamReader]::new($response.Content.ReadAsStream($deadline.Token))
            $null = Read-ChatStream $reader $deadline.Token
            $script:Steps.Add('Synthetic chat returned a validated reply after response.completed')
        } catch {
            if ($_.Exception.Message -match '^Inference failed \(HTTP [0-9]+\)\.$') { throw $_.Exception.Message }
            if ($deadline.IsCancellationRequested) { throw ('Inference exceeded the 20-second deadline during ' + $stage + '.') }
            $safeMessages = @('Inference did not return an event stream.', 'Event stream exceeded the verification limit.',
                'Invalid JSON.', 'Empty JSON.', 'Unexpected event after response completion.',
                'Inference output exceeded the verification limit.', 'Invalid response completion event.',
                'Inference failed or was incomplete.', 'Stream ended without response.completed.',
                'Inference did not return a valid synthetic chat reply.')
            if ($_.Exception.Message -cin $safeMessages) { throw $_.Exception.Message }
            # Type and source line provide diagnostics without exposing response/token text.
            throw ('Inference ' + $stage + ' failed (' + $_.Exception.GetType().Name +
                ', script line ' + $_.InvocationInfo.ScriptLineNumber + '); no response text was logged.')
        } finally {
            if ($reader) { $reader.Dispose() }; if ($response) { $response.Dispose() }
            $request.Dispose(); $deadline.Dispose()
        }
    } finally {
        if ($listener) { $listener.Stop() }
        if ($tokens -and $tokens.ContainsKey('refresh_token') -and $callback -and $discovery) {
            $revoke = New-FormRequest $discovery.revocation_endpoint @{
                token = $tokens.refresh_token; token_type_hint = 'refresh_token'; client_id = $callback.client_id
            }
            $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(20)); $response = $null
            try {
                $response = $http.SendAsync($revoke, $deadline.Token).GetAwaiter().GetResult()
                Assert-Http $response 'Session revocation'
                $script:Steps.Add('Renewable test session revoked; no credentials retained')
            } catch { throw 'Remote session revocation could not be confirmed. Disconnect Cat Quest OAuth Verification in ChatGPT settings.' }
            finally { if ($response) { $response.Dispose() }; $revoke.Dispose(); $deadline.Dispose(); $tokens.Clear() }
        }
        $http.Dispose()
    }
}

function Invoke-OfflineTests([string]$Directory) {
    function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
    function Reject([scriptblock]$Action, [string]$Message) {
        $rejected = $false
        try { $null = & $Action } catch { $rejected = $true }
        Check $rejected $Message
    }
    Check ((Get-Challenge 'dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk') -ceq 'E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM') 'RFC 7636 PKCE vector failed.'
    Check ((New-RandomToken) -cne (New-RandomToken)) 'Random values repeated.'
    $good = '/auth/callback?state=test&code=synthetic&client_id=oaiapp_test'
    Check ((Read-Callback $good 'test').code -ceq 'synthetic') 'Valid callback rejected.'
    Reject { Read-Callback $good 'other' } 'Wrong state accepted.'
    Reject { Read-Callback ($good + '&state=test') 'test' } 'Duplicate state accepted.'
    Reject { Read-Callback ($good.Replace('/auth/callback', '/other')) 'test' } 'Wrong path accepted.'
    Reject { Read-Callback '/auth/callback?state=test&code=synthetic' 'test' } 'Missing issued client accepted.'
    Reject { Read-Callback '/auth/callback?state=test&error=access_denied' 'test' } 'Declined consent accepted.'
    $script:Steps.Add('PKCE, randomness, callback path/state/duplicates/denial/client ID checks passed')
    $rsa = [Security.Cryptography.RSA]::Create(2048)
    try {
        $p = $rsa.ExportParameters($false)
        $jwks = @{ keys = @(@{ kid = 'test'; kty = 'RSA'; n = (ConvertTo-Base64Url $p.Modulus); e = (ConvertTo-Base64Url $p.Exponent) }) }
        $header = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"RS256","kid":"test"}'))
        $claims = @{ iss = $script:Issuer; aud = 'oaiapp_test'; sub = 'synthetic-user'; nonce = 'test-nonce'; exp = 2000 }
        function Sign([hashtable]$Value) {
            $unsigned = $header + '.' + (ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes(($Value | ConvertTo-Json -Compress))))
            return $unsigned + '.' + (ConvertTo-Base64Url ($rsa.SignData([Text.Encoding]::ASCII.GetBytes($unsigned),
                [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)))
        }
        $jwt = Sign $claims
        $null = Test-IdToken $jwt $jwks 'oaiapp_test' 'test-nonce' 1000
        Reject { Test-IdToken $jwt $jwks 'oaiapp_other' 'test-nonce' 1000 } 'Wrong audience accepted.'
        Reject { Test-IdToken $jwt $jwks 'oaiapp_test' 'other' 1000 } 'Wrong nonce accepted.'
        Reject { Test-IdToken $jwt $jwks 'oaiapp_test' 'test-nonce' 2000 } 'Expired token accepted.'
        $parts = $jwt.Split('.'); $signature = ConvertFrom-Base64Url $parts[2]; $signature[0] = $signature[0] -bxor 1
        $bad = $parts[0] + '.' + $parts[1] + '.' + (ConvertTo-Base64Url $signature)
        Reject { Test-IdToken $bad $jwks 'oaiapp_test' 'test-nonce' 1000 } 'Invalid signature accepted.'
        $claims.nbf = 3000; $future = Sign $claims
        Reject { Test-IdToken $future $jwks 'oaiapp_test' 'test-nonce' 1000 } 'Future token accepted.'
    } finally { $rsa.Dispose() }
    Reject { Test-PlanPermission @{ scope = 'openid'; token_type = 'Bearer'; access_token = 'test'; refresh_token = 'test' } } 'Missing plan permission accepted.'
    $script:Steps.Add('Signed ID token, audience, nonce, expiry, signature, not-before and permission checks passed')
    $state = New-StreamState
    $replyText = '{"message":"Hello","drafts":[]}'
    $delta = @{ type = 'response.output_text.delta'; delta = $replyText } | ConvertTo-Json -Compress
    Read-StreamEvent $state $delta
    Reject { Get-ChatReply $state } 'Partial output accepted.'
    Read-StreamEvent $state '{"type":"response.completed","response":{"status":"completed"}}'
    Check ((Get-ChatReply $state).message -ceq 'Hello') 'Valid chat rejected.'
    foreach ($type in @('response.failed', 'response.incomplete', 'error')) {
        Reject { Read-StreamEvent (New-StreamState) ('{"type":"' + $type + '"}') } ('Failure event accepted: ' + $type)
    }
    foreach ($text in @('{"message":"","drafts":[]}', '{"message":"Hello","drafts":[{}]}', 'not json')) {
        Reject { Get-ChatReply @{ text = $text; completed = $true } } 'Invalid chat accepted.'
    }
    $wire = "event: response.output_text.delta`r`ndata: $delta`r`n`r`nevent: response.completed`r`ndata: {`"type`":`"response.completed`",`"response`":{`"status`":`"completed`"}}`r`n`r`n"
    $stream = [IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes($wire))
    $reader = [IO.StreamReader]::new($stream)
    try { Check ((Read-ChatStream $reader ([Threading.CancellationToken]::None)).message -ceq 'Hello') 'SSE wire reader rejected valid CRLF events.' }
    finally { $reader.Dispose() }
    $response = [Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]::OK)
    $response.Content = [Net.Http.ByteArrayContent]::new([Text.Encoding]::UTF8.GetBytes($wire))
    try {
        Assert-StreamContent $response
        $reader = [IO.StreamReader]::new($response.Content.ReadAsStream())
        try { Check ((Read-ChatStream $reader ([Threading.CancellationToken]::None)).message -ceq 'Hello') 'Stream without Content-Type was rejected.' }
        finally { $reader.Dispose() }
        $response.Content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('text/event-stream')
        Assert-StreamContent $response
        $response.Content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('text/html')
        Reject { Assert-StreamContent $response } 'HTML response was accepted as a stream.'
    } finally { $response.Dispose() }
    $script:Steps.Add('Completed stream, partial output, failure events and malformed chat checks passed')
    $plain = [Text.Encoding]::UTF8.GetBytes('synthetic credential: never a real token')
    Test-ProtectedStorage (Join-Path $Directory 'offline-roundtrip.dpapi') $plain
    Check (-not [IO.File]::Exists((Join-Path $Directory 'offline-roundtrip.dpapi'))) 'Credential test file was retained.'
    $script:Steps.Add('Windows DPAPI, atomic write and credential cleanup passed')
}

if (-not $Run -and -not $SelfTest) {
    Write-Host 'Usage (PowerShell 7.4+ on Windows):'
    Write-Host '  pwsh -File Tools/Verify-ChatGptOAuth.ps1 -SelfTest'
    Write-Host '  pwsh -File Tools/Verify-ChatGptOAuth.ps1 -Run'
    exit 0
}
if (-not $IsWindows) { throw 'This verification requires Windows.' }
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$kind = if ($SelfTest) { 'Offline' } else { 'Live' }
$directory = Join-Path $workspace ('Temp/ChatGptVerification/' + $kind + '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($directory)
$report = [ordered]@{ startedUtc = [DateTime]::UtcNow.ToString('o'); mode = $kind; result = 'FAIL'; steps = @(); reason = ''; unityVerified = $false; productUiIntegrated = $false }
$exitCode = 1
try {
    if ($SelfTest) { Invoke-OfflineTests $directory } else { Invoke-LiveVerification $directory }
    $report.result = 'PASS'; $exitCode = 0
} catch {
    # Every network operation uses fixed diagnostic messages; never serialize Exception objects.
    $report.reason = $_.Exception.Message
    Write-Host ($kind + ' verification failed: ' + $report.reason)
} finally {
    $report.steps = @($script:Steps.ToArray()); $report['finishedUtc'] = [DateTime]::UtcNow.ToString('o')
    $path = Join-Path $directory 'result.json'
    [IO.File]::WriteAllText($path, ($report | ConvertTo-Json -Depth 8))
    Write-Host ('Result: ' + $report.result + '; report: ' + $path)
}
exit $exitCode
