;; Dumps model-space geometry and layers of the current drawing to a text file.
(defun dws:dump (path / f ss i e typ lay)
  (setq f (open path "w"))
  ;; layers
  (setq e (tblnext "LAYER" T))
  (while e
    (write-line (strcat "LAYER|" (cdr (assoc 2 e)) "|" (itoa (abs (cdr (assoc 62 e)))) "|" (itoa (cdr (assoc 70 e)))) f)
    (setq e (tblnext "LAYER")))
  (write-line (strcat "INSUNITS|" (itoa (getvar "INSUNITS"))) f)
  (write-line (strcat "EXTMIN|" (rtos (car (getvar "EXTMIN")) 2 3) "|" (rtos (cadr (getvar "EXTMIN")) 2 3)) f)
  (write-line (strcat "EXTMAX|" (rtos (car (getvar "EXTMAX")) 2 3) "|" (rtos (cadr (getvar "EXTMAX")) 2 3)) f)
  (setq ss (ssget "X" '((410 . "Model"))))
  (setq i 0)
  (if ss
    (repeat (sslength ss)
      (setq e (entget (ssname ss i)) typ (cdr (assoc 0 e)) lay (cdr (assoc 8 e)))
      (cond
        ((= typ "LWPOLYLINE")
         (write-line (strcat "PL|" lay "|" (itoa (cdr (assoc 70 e))) "|"
                             (apply 'strcat
                                    (mapcar '(lambda (x)
                                               (cond ((= (car x) 10) (strcat ";" (rtos (cadr x) 2 3) "," (rtos (caddr x) 2 3)))
                                                     ((= (car x) 42) (strcat "," (rtos (cdr x) 2 4)))
                                                     (T "")))
                                            e)))
                     f))
        ((= typ "CIRCLE")
         (write-line (strcat "CIRCLE|" lay "|" (rtos (cadr (assoc 10 e)) 2 3) "," (rtos (caddr (assoc 10 e)) 2 3) "|" (rtos (cdr (assoc 40 e)) 2 3)) f))
        ((or (= typ "TEXT") (= typ "MTEXT"))
         (write-line (strcat typ "|" lay "|" (rtos (cadr (assoc 10 e)) 2 3) "," (rtos (caddr (assoc 10 e)) 2 3) "|" (rtos (cdr (assoc 40 e)) 2 3) "|" (cdr (assoc 1 e))) f))
        (T (write-line (strcat "OTHER|" lay "|" typ) f)))
      (setq i (1+ i))))
  (close f)
  (princ))
