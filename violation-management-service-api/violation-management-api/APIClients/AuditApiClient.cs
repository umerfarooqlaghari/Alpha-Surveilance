using AlphaSurveilance.Audit.Grpc;
using Grpc.Core;
using System;
using System.Threading.Tasks;

namespace AlphaSurveilance
{
    // The AuditApiClient acts as a bridge between our domain logic and the external Audit API.
    // Instead of using HttpClient and JSON strings, we use a strongly-typed gRPC Client.
    public class AuditApiClient(AuditService.AuditServiceClient grpcClient, ILogger<AuditApiClient> logger) : IAuditApiClient
    {
        public async Task<bool> LogViolationAsync(Guid violationId, string tenantId, string type, DateTime timestamp, CancellationToken token)
        {
            try
            {
                // Data Flow: Domain Model -> gRPC Request Object -> Binary Protocol (HTTP/2)
                var request = new LogViolationRequest
                {
                    ViolationId = violationId.ToString(),
                    TenantId = tenantId,
                    ViolationType = type,
                    Timestamp = timestamp.ToString("O") // ISO 8601 format
                };

                logger.LogInformation("Sending gRPC Log Request: {ViolationId}", violationId);

                // This call triggers the "LogViolation" method on the Audit microservice.
                var response = await grpcClient.LogViolationAsync(request, cancellationToken: token);

                return response.Success;
            }
            catch (RpcException ex)
            {
                // gRPC uses specific error codes (e.g., Unavailable, Unauthenticated)
                logger.LogError(ex, "gRPC Call failed to Audit API. Code: {StatusCode}", ex.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error reaching Audit API");
                return false;
            }
        }
    }
}