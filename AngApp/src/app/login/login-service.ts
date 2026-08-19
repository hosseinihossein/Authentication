import { HttpClient, HttpErrorResponse, HttpEventType, HttpHeaders, HttpParams, HttpStatusCode } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { LoginModel } from './login';
import { FieldTree, TreeValidationResult } from '@angular/forms/signals';
import { WindowService } from '../shared/window-service';
import { ActivatedRoute } from '@angular/router';

@Service()
export class LoginService {
    //private readonly httpClient = inject(HttpClient);
    private readonly windowService = inject(WindowService);
    //private activatedRoute = inject(ActivatedRoute);



    async submitLogin(loginForm:FieldTree<LoginModel>){
        let loginFormSearchParams = new URLSearchParams();
        loginFormSearchParams.append("CfTurnstileResponse",loginForm.CfTurnstileResponse().value());
        loginFormSearchParams.append("UsernameOrEmail", loginForm.UsernameOrEmail().value());
        loginFormSearchParams.append("Password",loginForm.Password().value());
        loginFormSearchParams.append("ReturnUrl",loginForm.ReturnUrl().value());

        let res = await fetch("Api/Authentication/Login",{
        method: "POST",
        body: loginFormSearchParams,
        /*headers: { // set by default
            "Content-Type": "application/x-www-form-urlencoded",
        },*/
        });

        let result:TreeValidationResult|null = null;
        if(res.ok && res.url){
        this.windowService.nativeWindow.location.href = res.url;
        }
        else if(res.status === HttpStatusCode.BadRequest){
        let err = new HttpErrorResponse({error:await res.json()});
        if(err.error.Email || err.error.Username || err.error.errors?.UsernameOrEmail){
            result = {
            kind: "LoginError",
            message: err.error.Email || err.error.Username || err.error.errors?.UsernameOrEmail,
            fieldTree: loginForm.UsernameOrEmail,
            };
        }
        else if(err.error.Password || err.error.errors?.Password){
            result = {
            kind: "LoginError",
            message: err.error.Password || err.error.errors?.Password,
            fieldTree: loginForm.Password,
            };
        }
        else if(err.error.TurnstileError || err.error.errors?.CfTurnstileResponse){
            result = {
            kind: "LoginError",
            message: err.error.TurnstileError || err.error.errors?.CfTurnstileResponse,
            fieldTree: loginForm.CfTurnstileResponse,
            };
        }
        else{
            result = {
            kind: "LoginError",
            message: JSON.stringify(err.error,null,2),
            fieldTree: loginForm,
            };
        }
        }
        return result;
    }

}
