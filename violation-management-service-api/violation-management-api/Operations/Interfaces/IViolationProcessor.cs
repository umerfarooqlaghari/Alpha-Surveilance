using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AlphaSurveilance
{
public interface IViolationProcessor
{
    Task<bool> ProcessAsync(string messageBody, CancellationToken token);
}

}