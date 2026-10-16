using System;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace plcMSVCplugin
{
    [Guid("e08a6fb7-b129-4d6f-96a2-a1f9e9cfb301")]
    public class PlanInspectorToolWindow : ToolWindowPane
    {
        private readonly PlanInspectorControl _control;
        private DebuggerEvents? _debuggerEvents;
        private DTE2? _dte;

        public static PlanInspectorToolWindow? Instance { get; private set; }

        public PlanInspectorControl Control => _control;

        public PlanInspectorToolWindow() : base(null)
        {
            Caption = "Инспектор плана и потоков Planning C";
            _control = new PlanInspectorControl();
            Content = _control;
            Instance = this;
        }

        protected override void Initialize()
        {
            base.Initialize();
            ThreadHelper.ThrowIfNotOnUIThread();

            if (ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is DTE2 dte && dte.Events?.DebuggerEvents != null)
            {
                _dte = dte;
                _debuggerEvents = dte.Events.DebuggerEvents;
                _debuggerEvents.OnEnterBreakMode += DebuggerEvents_OnEnterBreakMode;
                _debuggerEvents.OnEnterRunMode += DebuggerEvents_OnEnterRunMode;
                _debuggerEvents.OnContextChanged += DebuggerEvents_OnContextChanged;
            }
        }

        private void DebuggerEvents_OnEnterBreakMode(dbgEventReason Reason, ref dbgExecutionAction ExecutionAction)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_dte == null) return;

            string reasonText = Reason switch
            {
                dbgEventReason.dbgEventReasonBreakpoint => "Сработала точка останова",
                dbgEventReason.dbgEventReasonStep => "Шаг отладки (Step)",
                dbgEventReason.dbgEventReasonExceptionThrown => "Исключение (Exception)",
                dbgEventReason.dbgEventReasonUserBreak => "Приостановлено пользователем",
                _ => Reason.ToString()
            };

            // Автоматическая активация окна, если включена опция
            if (plcMSVCpluginPackage.Instance != null)
            {
                var options = (PlanningCOptionsPage)plcMSVCpluginPackage.Instance.GetDialogPage(typeof(PlanningCOptionsPage));
                if (options.AutoOpenInspectorOnBreak)
                {
                    var windowFrame = Frame as IVsWindowFrame;
                    windowFrame?.Show();
                }
            }

            if (_control.AutoRefreshEnabled)
            {
                _control.UpdateDebuggerState(_dte, reasonText);
            }
        }

        private void DebuggerEvents_OnEnterRunMode(dbgEventReason Reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_dte == null) return;

            _control.UpdateDebuggerState(_dte);
        }

        private void DebuggerEvents_OnContextChanged(Process NewProcess, Program NewProgram, Thread NewThread, StackFrame NewStackFrame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_dte == null) return;

            if (_dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode && _control.AutoRefreshEnabled)
            {
                _control.UpdateDebuggerState(_dte);
            }
        }

        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposing && _debuggerEvents != null)
            {
                _debuggerEvents.OnEnterBreakMode -= DebuggerEvents_OnEnterBreakMode;
                _debuggerEvents.OnEnterRunMode -= DebuggerEvents_OnEnterRunMode;
                _debuggerEvents.OnContextChanged -= DebuggerEvents_OnContextChanged;
                _debuggerEvents = null;
            }
            if (Instance == this) Instance = null;
            base.Dispose(disposing);
        }
    }
}
