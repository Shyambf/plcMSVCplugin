using System;
using System.ComponentModel.Design;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace plcMSVCplugin
{
    /// <summary>
    /// Команда "Очистить файлы сборки Planning C (.plc_build)".
    /// </summary>
    internal sealed class CleanPlcBuildCommand
    {
        public const int CommandId = 0x0103;
        public static readonly Guid CommandSet = new Guid("298516d5-a337-4bf7-bfeb-433a08892f25");

        private readonly AsyncPackage _package;

        private CleanPlcBuildCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static CleanPlcBuildCommand? Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new CleanPlcBuildCommand(package, commandService!);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(DTE)) is DTE2 dte)
            {
                var launcher = new PlanningCDebuggerLauncher(_package, dte);
                _ = launcher.CleanBuildFilesAsync();
            }
        }
    }
}
