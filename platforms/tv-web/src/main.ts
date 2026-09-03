import "./styles.css";
import { OrbitalVueTvApp } from "./ui/OrbitalVueTvApp.js";

const root = document.querySelector<HTMLElement>("#app");
if (!root) throw new Error("OrbitalVue could not create its television surface.");

const app = new OrbitalVueTvApp(root);
void app.start();
