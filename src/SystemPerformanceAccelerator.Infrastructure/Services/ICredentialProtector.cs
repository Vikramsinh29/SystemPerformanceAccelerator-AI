namespace SystemPerformanceAccelerator.Infrastructure.Services;

public interface ICredentialProtector
{
    byte[] Protect(byte[] plaintext);

    byte[] Unprotect(byte[] protectedData);
}
