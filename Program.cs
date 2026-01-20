using TicTacToe.Components;
using TicTacToe.Services;
using TicTacToe.Hubs;

namespace TicTacToe
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Services.AddSingleton<GameService>();

            builder.Services.AddSignalR();

            var app = builder.Build();

            /*

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            
              
            wylaczylem szyfrowanie, bo testowalem rozgrywke na roznych komputerach w sieci lokalnej i vpn, 
            a przez to pojawial sie problem z certyfikatem, który by³ wystawiony dla localhost
            
            */

            //app.UseHttpsRedirection();

            app.UseExceptionHandler("/Error");
            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

            

            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.MapHub<GameHub>("/gamehub");

            app.Run();
        }
    }
}
