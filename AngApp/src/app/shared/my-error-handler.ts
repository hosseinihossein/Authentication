import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { inject, Injectable, Service } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

@Service()//@Injectable({ providedIn: 'root' })
export class MyErrorHandler {
    private snackBar = inject(MatSnackBar);

    handleError(error: any): void {
        const err = error.rejection || error;
        let errTypeMessage = "";

        if(err instanceof HttpErrorResponse){
            switch(err.status){
                case 0:
                    errTypeMessage = "Connection Error";
                    break;
                case HttpStatusCode.Unauthorized:
                    errTypeMessage = "Unauthorized";
                    //this.identityService.logout();
                    break;
                case HttpStatusCode.Forbidden:
                    errTypeMessage = "Access Denied";
                    break;
                case HttpStatusCode.BadRequest:
                    errTypeMessage = "Bad Request";
                    break;
                case 403://Forbidden
                    errTypeMessage = "Forbidden";
                    break;
                default:
                    errTypeMessage = "Unknown Error";
            }
        }
        else{
            errTypeMessage = "Application Error";
        }

        this.snackBar.open(errTypeMessage, "Ok"/*, { duration: 5000 }*/);
        //console.error("in my-error-handler");
        console.error(errTypeMessage, err);
    }
}
