Usage/rules:

- The tool DbTools is used to autogenerate a Yuniql deploy file for functions and procedures. Other artifacts
  like tables and index must currently be maintained manually.

- Each function/proc has a separate file in the Migration/FunctionsAndProcedures folder.

- After build the tool DbTools.exe is automatically run to generate vx.xx/ZZ-functions-and-procedures.sql
  based on the content in all files in the FunctionsAndProcedures folder. ZZ is assumed to be 01 if no other
  file is found with another number. (Makes it possible to deploy other stuff before procs/funcs.)

- The file name of a proc/func should be the base proc/function name without any version postfix.
  The same filename is kept if the proc/func gets a new version.

- Any drop commands must be coded at the top of the func/proc file or in a separate file in the related v.x.xx folder.

- A new vx.xx folder must be created when a func/proc is created/updated after last deploy. If not the current
  vx.xx will be used, and the migration will not be executed by yuniql.