using Grpc.Core;
using AlphaSurveilance.Bff.Grpc;
using alpha_surveilance_bff.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace alpha_surveilance_bff.Services
{
    // The NotificationGrpcService is the "Receiver" for Backend Services.
    // When ViolationManagement detects a violation, it calls this service.
    public class NotificationGrpcService(
        IHubContext<ViolationHub> hubContext,
        ILogger<NotificationGrpcService> logger) : NotificationService.NotificationServiceBase
    {
        public override async Task<ViolationNotificationResponse> PushViolation(
            ViolationNotificationRequest request, 
            ServerCallContext context)
        {
            // Data Flow: Violation Service -> BFF gRPC -> SignalR Hub -> React UI
            logger.LogInformation("Pushing real-time notification for tenant {TenantId}", request.TenantId);

            // Broadcast the violation to all users in the specific Tenant Group.
            // This ensures Tenant A never sees Tenant B's real-time violations.
            await hubContext.Clients.Group(request.TenantId).SendAsync("ReceiveViolation", new
            {
                request.Id,
                request.Type,
                request.Severity,
                request.Timestamp,
                request.FramePath,
                request.CameraId
            });

            return new ViolationNotificationResponse { Acknowledged = true };
        }
    }
}
