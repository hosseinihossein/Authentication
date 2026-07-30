import { HttpClient } from '@angular/common/http';
import { afterRenderEffect, effect, inject, Service, signal, WritableSignal } from '@angular/core';
import { SingletonService } from './singleton-service';
import { FieldTree } from '@angular/forms/signals';
import { LoginModel } from '../login/login';
import { WindowService } from './window-service';

declare const turnstile:any;

@Service()
export class TurnstileService {
    private readonly httpClient = inject(HttpClient);
    private singleton = inject(SingletonService);
    //private windowService = inject(WindowService);
    
    enableTurnstile = signal(false);
    readonly turnstileSiteKey = "0x4AAAAAAAkeZ2wTzJxqgC_K";

    widgetId = signal("");
    
    constructor(){
        this.checkTurnstile();
    }

    checkTurnstile(){
        this.httpClient.get<{enableTurnstile:boolean}>("/Identity/Api/User/EnableTurnstile").subscribe({
            next: res => {
                //console.log(res);
                if(res && res.enableTurnstile === true){
                    this.enableTurnstile.set(true);
                }
                else{
                    this.enableTurnstile.set(false);
                }
            },
        });
    }

    loadWidget(loginForm:FieldTree<LoginModel>, errorMessageSignal:WritableSignal<string>){
        this.widgetId.set(
            turnstile.render("#widget-container", {
            sitekey: this.turnstileSiteKey,
            size: "flexible",
            theme: this.singleton.darkMode() ? "dark" : "light",
            "response-field": false,
            action: "login",
            "refresh-expired": "manual",
            "refresh-timeout": "manual",
            callback: (token:string) => {
                loginForm.CfTurnstileResponse().value.set(token);
            },
            'error-callback': (errorCode: string) => {
                errorMessageSignal.set("Turnstile error! error code: " + errorCode);
                console.error("turnstile error-callback: " + errorCode);
            },
            'expired-callback': () => {
                errorMessageSignal.set("Turnstile expired!");
                console.error("turnstile expired-callback");
            },
            'timeout-callback': () => {
                errorMessageSignal.set("Turnstile timeouted!")
                console.error("turnstile timeout-callback");
            },
            })
        );
    }

    reset(){
        turnstile.reset(this.widgetId());
    }

}
