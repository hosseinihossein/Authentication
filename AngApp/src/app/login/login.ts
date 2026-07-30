import { afterRenderEffect, AfterViewChecked, AfterViewInit, Component, effect, ElementRef, inject, signal, viewChild, viewChildren } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { LoginService } from './login-service';
import { NgOptimizedImage } from '@angular/common';
import { SingletonService } from '../shared/singleton-service';
import { form, FormField, FormRoot, hidden, minLength, required, TreeValidationResult } from '@angular/forms/signals';
import { MatFormField, MatFormFieldModule } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatIcon } from "@angular/material/icon";
import { WaitSpinner } from '../shared/wait-spinner/wait-spinner';
import { MatButton, MatFabButton } from '@angular/material/button';
import { MatTooltip } from '@angular/material/tooltip';
import { TurnstileService } from '../shared/turnstile-service';

export interface LoginModel {
  UsernameOrEmail: string,
  Password:string,
  CfTurnstileResponse: string,
  ReturnUrl: string,
}

@Component({
  selector: 'app-login',
  imports: [FormField, FormRoot, MatFormFieldModule, MatInput, MatIcon, WaitSpinner, MatButton, RouterLink,
    NgOptimizedImage, MatFabButton, MatTooltip],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  loginService = inject(LoginService);
  singleton = inject(SingletonService);
  private activatedRoute = inject(ActivatedRoute);
  turnstileService = inject(TurnstileService);

  displayWait = signal(false);
  widgetId = signal("");
  generalError = signal<string>("");

  loginModel = signal<LoginModel>({
    UsernameOrEmail: "",
    Password: "",
    CfTurnstileResponse: "",
    ReturnUrl: this.activatedRoute.snapshot.queryParamMap.get("ReturnUrl") || "/",
  });
  loginForm = form(this.loginModel,
    (schemaPath)=>{
      required(schemaPath.UsernameOrEmail, {message:"Username or Email is required!"});
      
      required(schemaPath.Password, {message:"Password is required!"});
      minLength(schemaPath.Password, 5, {message:"Password must be at least 5 characters!"});
    },
    {
      submission: {
        action: async (field) => {
          this.displayWait.set(true);
          let result = await this.loginService.submitLogin(field);
          this.displayWait.set(false);
          if(result){
            this.turnstileService.reset();
          }
          return result;
        },
      }
    }
  );

  constructor(){
    afterRenderEffect({
      write:()=>{
        if(this.turnstileService.enableTurnstile() && !this.turnstileService.widgetId()){
          this.turnstileService.loadWidget(this.loginForm,this.generalError);
        }
      },
    });
  }

  forgetPassword(){}


}
