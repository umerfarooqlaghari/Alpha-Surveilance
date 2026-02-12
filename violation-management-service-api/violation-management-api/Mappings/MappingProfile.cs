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
                .ForMember(dest => dest.Type, opt => opt.MapFrom(src => Enum.Parse<ViolationType>(src.Type, true)))
                .ForMember(dest => dest.Severity, opt => opt.MapFrom(src => Enum.Parse<ViolationSeverity>(src.Severity, true)))
                .ForMember(dest => dest.MetadataJson, opt => opt.MapFrom(src => src.MetadataJson))
                .ForMember(dest => dest.Timestamp, opt => opt.MapFrom(src => DateTime.SpecifyKind(src.Timestamp, DateTimeKind.Utc))) // Enforce UTC
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.Status, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore());

            // API DTO -> Domain
            CreateMap<ViolationRequest, Violation>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.Status, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore());

            // Domain -> DTO
            CreateMap<Violation, ViolationResponse>();
        }
    }
}
