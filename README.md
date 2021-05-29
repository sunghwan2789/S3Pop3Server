# S3 POP3 Server

A POP3 server uses Amazon S3 bucket as a mailbox and implements [RFC 1939](https://www.ietf.org/rfc/rfc1939.txt).

## Configuration

### User Secrets (on Development)

```
dotnet user-secrets set AWS:AccessKey ACCESS_KEY
dotnet user-secrets set AWS:SecretKey SECRET_ACCESS_KEY
dotnet user-secrets set AWS:Region REGION
```

### Environment Variables (on Production)

```
export AWS__ACCESSKEY=ACCESS_KEY
export AWS__SECRETKEY=SECRET_ACCESS_KEY
export AWS__REGION=REGION
```

## Security Concerns

Keep secure the server by running it behind a firewall and whitelisting client ip addresses.
