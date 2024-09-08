using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

using HomeAppsLib;

namespace HomeWebApp
{
    public partial class ForgotPassword : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {

        }

        protected void btnSubmit_Click(object sender, EventArgs e)
        {
            string body = string.Empty;            

            IQueryable<HomeAppsLib.db.user> users = HomeAppsLib.LibCommon.DBModel().users.Where(u => u.email == tbEmail.Text && u.IsActive);

            if (users != null && users.Any())
            {
                body += "Your login credentials are below:<br>";

                foreach (var user in users)
                {
                    body += "Name: " + user.name + "<br>";
                    body += "Pw: " + new Encryption().Decrypt(user.encPW) + "<br><br>";
                }

                HomeAppsLib.LibCommon.SendSystemEmailWithSignature(users.First().email, "Credentials requested", body);

                lblStatus.Text = "We have sent your login info to your registered email address.";

            }
            else
            {
                body += "Name: " + tbName.Text + "<br>";
                body += "Email: " + tbEmail.Text + "<br>";

                Common.SendEmail("eric@hackerdevs.com", "User requesting credentials", body, tbName.Text);
                lblStatus.Text = "Request sent to system admin... Eric will respond under the Wright family tradition of \"right here directly\", or otherwise known as when he acquires a Round Toit.";
            }
        }
    }
}