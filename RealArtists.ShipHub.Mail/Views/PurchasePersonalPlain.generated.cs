﻿#pragma warning disable 1591
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace RealArtists.ShipHub.Mail.Views
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    
    [System.CodeDom.Compiler.GeneratedCodeAttribute("RazorGenerator", "2.0.0.0")]
    public partial class PurchasePersonalPlain : RealArtists.ShipHub.Mail.ShipHubTemplateBase<RealArtists.ShipHub.Mail.Models.PurchasePersonalMailMessage>
    {
#line hidden
        public override void Execute()
        {
WriteLiteral("\r\n");

            
            #line 3 "..\..\Views\PurchasePersonalPlain.cshtml"
   
  Layout = new RealArtists.ShipHub.Mail.Views.LayoutPlain() { Model = Model };

            
            #line default
            #line hidden
WriteLiteral("\r\nThanks for purchasing a subscription to Ship - we hope you enjoy using it!\r\n\r\nA" +
"ttached is an invoice receipt for your records.\r\n");

            
            #line 9 "..\..\Views\PurchasePersonalPlain.cshtml"
 if (Model.WasGivenTrialCredit) {

            
            #line default
            #line hidden
WriteLiteral("\r\nA discount was applied to your first invoice becuase you still had some time re" +
"maining on your free trial.  Next month you\'ll see the regular price of $9/month" +
".\r\n");

            
            #line 12 "..\..\Views\PurchasePersonalPlain.cshtml"
       }

            
            #line default
            #line hidden
            
            #line 13 "..\..\Views\PurchasePersonalPlain.cshtml"
 if (Model.BelongsToOrganization) {

            
            #line default
            #line hidden
WriteLiteral("\r\n# Want to use Ship for free?\r\n\r\nYour personal Ship subscription is free as long" +
" as you belong to an organization that subscribes to Ship.  Ask your organizatio" +
"n to sign up.\r\n");

WriteLiteral("\r\n");

            
            #line 19 "..\..\Views\PurchasePersonalPlain.cshtml"
}

            
            #line default
            #line hidden
WriteLiteral("# How to manage your account:\r\n\r\nIf you need to change billing or payment info, o" +
"r need to cancel your account, you can do so from within the Ship application. F" +
"rom the \"Ship\" menu, choose \"Manage Subscription\". Then click \"Manage\" for your " +
"account.\r\n");

        }
    }
}
#pragma warning restore 1591
