using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace Sample_Net48.Order.Consumer
{
    [RunInstaller(true)]
    public class ProjectInstaller : Installer
    {
        private ServiceProcessInstaller processInstaller;
        private ServiceInstaller serviceInstaller;

        public ProjectInstaller()
        {
            processInstaller = new ServiceProcessInstaller();
            serviceInstaller = new ServiceInstaller();

            processInstaller.Account = ServiceAccount.LocalSystem;

            serviceInstaller.ServiceName = "Service1"; // Buraya kendi servis adınızı yazın
            serviceInstaller.StartType = ServiceStartMode.Manual;

            Installers.Add(processInstaller);
            Installers.Add(serviceInstaller);
        }
    }

}
