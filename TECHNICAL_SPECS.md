# Requisitos Técnicos y Compatibilidad - CodeCraft Optimizer

Este documento detalla los requisitos específicos y las restricciones de instalación para el correcto funcionamiento de la herramienta.

## Requisitos de Software

### Sistemas Operativos Soportados
*   **Windows 11:** Todas las versiones (Home, Pro, Enterprise).
*   **Windows 10:** Versión 1809 (October 2018 Update) o posterior.

### Dependencias Críticas
1.  **Microsoft .NET 8.0 Desktop Runtime:** La aplicación está construida sobre .NET 8. Es indispensable tener instalado el runtime de escritorio.
2.  **Windows Management Instrumentation (WMI):** El servicio de WMI debe estar habilitado y ejecutándose (es el estándar en Windows) para el monitoreo de hardware.

## Requisitos de Hardware

| Componente | Mínimo | Recomendado |
| :--- | :--- | :--- |
| **Procesador** | 1.0 GHz (Dual Core) | 2.5 GHz+ (Quad Core) |
| **Memoria RAM** | 1 GB | 4 GB+ |
| **Gráficos** | DirectX 9 con controlador WDDM 1.0 | DirectX 12 o superior |
| **Almacenamiento** | 50 MB libres | 100 MB libres (para logs y caché) |

## Restricciones de Instalación (Incompatibilidad)

*   **Sistemas Operativos:** No se puede instalar en Windows 7, Windows 8, Windows XP, ni en sistemas basados en Unix (macOS, Ubuntu, Android, etc.).
*   **Modo S de Windows:** La aplicación no funcionará en Windows 10/11 en "Modo S" ya que requiere permisos de sistema para el análisis de registro.
*   **Virtualización:** Puede presentar lecturas de hardware limitadas si se ejecuta dentro de máquinas virtuales (VirtualBox, VMware) sin passthrough de hardware.

## Permisos Requeridos

Para que la función de **Optimización** (gestión de programas de inicio) funcione, el usuario debe ejecutar la aplicación con **Privilegios de Administrador**. Sin estos permisos, la aplicación solo funcionará en modo de "Solo Lectura" para el monitoreo.
