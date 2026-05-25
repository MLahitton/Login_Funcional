window.facebookAuth = {
    _initialized: false,
    _initPromise: null,

    init: function (appId) {
        if (!appId) {
            return Promise.reject("Facebook AppId no configurado.");
        }

        if (this._initPromise) {
            return this._initPromise;
        }

        this._initPromise = new Promise((resolve, reject) => {
            const finishInit = () => {
                try {
                    window.FB.init({
                        appId: appId,
                        cookie: false,
                        xfbml: false,
                        version: "v21.0"
                    });
                    this._initialized = true;
                    resolve();
                } catch (error) {
                    reject(error?.message ?? "Error al inicializar Facebook SDK.");
                }
            };

            if (window.FB) {
                finishInit();
                return;
            }

            window.fbAsyncInit = finishInit;

            if (!document.getElementById("facebook-jssdk")) {
                const script = document.createElement("script");
                script.id = "facebook-jssdk";
                script.async = true;
                script.defer = true;
                script.crossOrigin = "anonymous";
                script.src = "https://connect.facebook.net/es_ES/sdk.js";
                script.onerror = () =>
                    reject("No se pudo cargar el SDK de Facebook. Revisa bloqueadores o conexion.");
                document.body.appendChild(script);
                return;
            }

            let attempts = 0;
            const waitForSdk = window.setInterval(() => {
                attempts++;
                if (window.FB) {
                    window.clearInterval(waitForSdk);
                    finishInit();
                    return;
                }

                if (attempts >= 100) {
                    window.clearInterval(waitForSdk);
                    reject("Facebook SDK no respondio. Recarga la pagina e intenta de nuevo.");
                }
            }, 100);
        });

        return this._initPromise;
    },

    login: async function (appId) {
        await this.init(appId);

        return new Promise((resolve, reject) => {
            let settled = false;

            const finish = (action, value) => {
                if (settled) {
                    return;
                }
                settled = true;
                window.clearTimeout(timeoutId);
                action(value);
            };

            const timeoutId = window.setTimeout(() => {
                finish(reject, "Inicio de sesion cancelado o tiempo agotado.");
            }, 120000);

            window.FB.login(
                (response) => {
                    if (response?.authResponse?.accessToken) {
                        finish(resolve, response.authResponse.accessToken);
                        return;
                    }

                    const fbError = response?.error?.message ?? response?.error_message;
                    if (fbError) {
                        finish(reject, fbError);
                        return;
                    }

                    if (response?.status === "not_authorized") {
                        finish(
                            reject,
                            "No autorizaste la app. En Meta debes ser Administrador o Tester y la app en modo desarrollo."
                        );
                        return;
                    }

                    // unknown = usuario cerro el popup o cancelo
                    finish(reject, "Inicio de sesion con Facebook cancelado.");
                },
                { scope: "public_profile,email", auth_type: "rerequest" }
            );
        });
    }
};
