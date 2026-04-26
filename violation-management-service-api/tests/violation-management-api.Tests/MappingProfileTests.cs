using System;
using AlphaSurveilance.Mappings;
using AutoMapper;
using Xunit;

namespace violation_management_api.Tests
{
    public class MappingProfileTests
    {
        private readonly IMapper _mapper;

        public MappingProfileTests()
        {
            var configuration = new MapperConfiguration(cfg =>
            {
                cfg.AddProfile<MappingProfile>();
            });

            _mapper = configuration.CreateMapper();
        }

        [Fact]
        public void AutoMapper_Configuration_IsValid()
        {
            // Assert
            // This verifies that all properties on destination types are mapped 
            // from the source type, or explicitly ignored.
            _mapper.ConfigurationProvider.AssertConfigurationIsValid();
        }
    }
}
