using System;
using System.Globalization;
using System.Security;

namespace WinMemoryCleaner
{
    // Pure XML construction so issue #179 can be tested without changing scheduled tasks.
    internal static class StartupTask
    {
        internal static string CreateXml(string title, string executablePath, string userSid, string userName, DateTime createdAt)
        {
            return string.Format(CultureInfo.InvariantCulture,
                @"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Author>{3}</Author>
    <Description>Runs {0} at logon.</Description>
    <Date>{4}</Date>
  </RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{2}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <WaitTimeout>PT10M</WaitTimeout>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author""><Exec><Command>""{1}""</Command></Exec></Actions>
</Task>",
                SecurityElement.Escape(title),
                SecurityElement.Escape(executablePath),
                SecurityElement.Escape(userSid),
                SecurityElement.Escape(string.Format(CultureInfo.InvariantCulture, "{0} ({1})", title, userName)),
                createdAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
        }
    }
}
