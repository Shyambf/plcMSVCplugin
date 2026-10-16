using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace plcMSVCplugin
{
    /// <summary>
    /// AsyncPackage расширения Visual Studio для поддержки Planning C 2.0 и отладки.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(plcMSVCpluginPackage.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(PlanInspectorToolWindow), Style = VsDockStyle.Tabbed, Window = ToolWindowGuids.Outputwindow)]
    [ProvideOptionPage(typeof(PlanningCOptionsPage), "Planning C", "Debugger and Translator", 0, 0, true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class plcMSVCpluginPackage : AsyncPackage
    {
        public const string PackageGuidString = "85b3e7d6-09cd-4249-82da-621c5de9eca6";

        public static plcMSVCpluginPackage? Instance { get; private set; }

        #region Package Members

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Instance = this;

            // Переключение на главный UI поток
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Регистрация команд
            await StartPlanningCDebugCommand.InitializeAsync(this);
            await RunPlanningCCommand.InitializeAsync(this);
            await CleanPlcBuildCommand.InitializeAsync(this);
            await OpenPlanInspectorCommand.InitializeAsync(this);
        }

        #endregion
    }
}
