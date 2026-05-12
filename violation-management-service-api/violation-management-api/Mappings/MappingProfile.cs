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
                .ForMember(dest => dest.Timestamp, opt => opt.MapFrom(src => DateTime.SpecifyKind(src.Timestamp, DateTimeKind.Utc))) // Enforce UTC
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.Status, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.SopViolationTypeId, opt => opt.Ignore())
                .ForMember(dest => dest.SopViolationType, opt => opt.Ignore());

            // API DTO -> Domain
            CreateMap<ViolationRequest, Violation>()
                .ForMember(dest => dest.TenantId, opt => opt.MapFrom(src => Guid.Parse(src.TenantId)))
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.Status, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.SopViolationTypeId, opt => opt.Ignore())
                .ForMember(dest => dest.SopViolationType, opt => opt.Ignore());

            CreateMap<Violation, ViolationResponse>()
                .ForMember(dest => dest.CameraName, opt => opt.Ignore()) // Populated via service enrichment
                .ForMember(dest => dest.SopName, opt => opt.MapFrom(src => 
                    src.SopViolationType != null && src.SopViolationType.Sop != null ? src.SopViolationType.Sop.Name : "Generic"))
                .ForMember(dest => dest.ViolationTypeName, opt => opt.MapFrom(src => 
                    src.SopViolationType != null ? src.SopViolationType.Name : "Generic"))
                .ForMember(dest => dest.ModelIdentifier, opt => opt.MapFrom(src => 
                    src.SopViolationType != null ? src.SopViolationType.ModelIdentifier : "Unknown"))
                .ForMember(dest => dest.Employee, opt => opt.MapFrom(src => 
                    src.Employee != null ? AlphaSurveilance.Extensions.EmployeeExtensions.ToResponse(src.Employee) : null));
        }
    }
}
