using AutoMapper;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Core.Enums;
using AlphaSurveilance.DTO.Requests;
using AlphaSurveilance.DTOs.Requests;
using AlphaSurveilance.DTOs.Responses;
using System;

namespace AlphaSurveilance.Mappings
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // Vision Ingestion -> Domain
            CreateMap<ViolationPayload, Violation>()
                .ForMember(dest => dest.TenantId, opt => opt.MapFrom(src => Guid.Parse(src.TenantId)))
                .ForMember(dest => dest.MetadataJson, opt => opt.MapFrom(src => src.MetadataJson))
                // Enforce UTC without shifting wall-clock time incorrectly:
                // Local-kind values (produced when System.Text.Json parses an ISO
                // string with an offset like "+00:00") are converted, already-UTC
                // values pass through, and Unspecified is assumed to be UTC.
                .ForMember(dest => dest.Timestamp, opt => opt.MapFrom(src => ToUtc(src.Timestamp)))
                // A freshly created violation was, by definition, last seen at its detection time.
                .ForMember(dest => dest.LastSeenAt, opt => opt.MapFrom(src => (DateTime?)ToUtc(src.Timestamp)))
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => ParseStatus(src.Status)))
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.SopViolationTypeId, opt => opt.Ignore())
                .ForMember(dest => dest.SopViolationType, opt => opt.Ignore())
                .ForMember(dest => dest.Employee, opt => opt.Ignore())  // resolved by service via EmployeeId FK
                // FP fields are set explicitly by the Mark endpoint, never from inbound payloads
                .ForMember(dest => dest.IsFalsePositive, opt => opt.Ignore())
                .ForMember(dest => dest.FalsePositiveMarkedAt, opt => opt.Ignore())
                .ForMember(dest => dest.FalsePositiveMarkedBy, opt => opt.Ignore())
                .ForMember(dest => dest.FalsePositiveReason, opt => opt.Ignore());

            // API DTO -> Domain
            CreateMap<ViolationRequest, Violation>()
                .ForMember(dest => dest.TenantId, opt => opt.MapFrom(src => Guid.Parse(src.TenantId)))
                // Same UTC normalisation as the payload map — Npgsql rejects
                // non-UTC kinds for timestamptz columns.
                .ForMember(dest => dest.Timestamp, opt => opt.MapFrom(src => ToUtc(src.Timestamp)))
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.Status, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.SopViolationTypeId, opt => opt.Ignore())
                .ForMember(dest => dest.SopViolationType, opt => opt.Ignore())
                .ForMember(dest => dest.LocationId, opt => opt.Ignore()) // camera-denormalised; set by service
                .ForMember(dest => dest.EmployeeId, opt => opt.Ignore()) // set by reid PATCH
                .ForMember(dest => dest.Employee, opt => opt.Ignore())   // resolved by EF navigation
                .ForMember(dest => dest.TrackId, opt => opt.Ignore())    // vision-service payload only
                .ForMember(dest => dest.LastSeenAt, opt => opt.Ignore()) // maintained by internal PATCH endpoint
                .ForMember(dest => dest.IsFalsePositive, opt => opt.Ignore())
                .ForMember(dest => dest.FalsePositiveMarkedAt, opt => opt.Ignore())
                .ForMember(dest => dest.FalsePositiveMarkedBy, opt => opt.Ignore())
                .ForMember(dest => dest.FalsePositiveReason, opt => opt.Ignore());

            CreateMap<Violation, ViolationResponse>()
                .ForMember(dest => dest.CameraName, opt => opt.Ignore()) // Populated via service enrichment
                .ForMember(dest => dest.CameraDeleted, opt => opt.Ignore()) // Populated via service enrichment
                .ForMember(dest => dest.FrameUrl, opt => opt.Ignore()) // Populated via S3 pre-signed URL in service
                .ForMember(dest => dest.SopName, opt => opt.MapFrom(src => 
                    src.SopViolationType != null && src.SopViolationType.Sop != null ? src.SopViolationType.Sop.Name : "Generic"))
                .ForMember(dest => dest.ViolationTypeName, opt => opt.MapFrom(src => 
                    src.SopViolationType != null ? src.SopViolationType.Name : "Generic"))
                .ForMember(dest => dest.ModelIdentifier, opt => opt.MapFrom(src => 
                    src.SopViolationType != null ? src.SopViolationType.ModelIdentifier : "Unknown"))
                .ForMember(dest => dest.Employee, opt => opt.MapFrom(src =>
                    src.Employee != null ? AlphaSurveilance.Extensions.EmployeeExtensions.ToResponse(src.Employee) : null));
        }

        /// <summary>
        /// Normalises an inbound DateTime to a UTC-kind value without corrupting it:
        ///  - Utc         → returned unchanged
        ///  - Local       → converted with ToUniversalTime() (System.Text.Json yields
        ///                  Local kind for ISO-8601 strings carrying an offset, e.g. "+00:00")
        ///  - Unspecified → assumed to already be UTC (SpecifyKind, no shift)
        /// </summary>
        public static DateTime ToUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        /// <summary>
        /// Parses the vision service's status string ("Pending", ...) into
        /// <see cref="AuditStatus"/>. Unknown/absent values fall back to Pending
        /// so a malformed payload can never skip the audit workflow.
        /// </summary>
        public static AuditStatus ParseStatus(string? status) =>
            !string.IsNullOrWhiteSpace(status) && Enum.TryParse<AuditStatus>(status, true, out var parsed)
                ? parsed
                : AuditStatus.Pending;
    }
}
