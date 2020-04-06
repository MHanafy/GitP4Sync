using System.Management.Automation;
using System.Threading.Tasks;

namespace GitP4Sync.Services
{
    public interface IScriptService
    {
        Task Init();
        Task<PSDataCollection<PSObject>> Execute(string script, bool logResult = false);
    }
}